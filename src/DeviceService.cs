using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using iMobileDevice;
using iMobileDevice.iDevice;
using iMobileDevice.Lockdown;
using iMobileDevice.Plist;
using iMobileDevice.Recovery;

namespace iDeviceRecovery;

public sealed class NormalInfo
{
    public string ProductType = "";
    public string Version = "";
    public string DeviceName = "";
    public ulong Ecid;
}

public enum EnterStatus { Ok, NeedsTrust, NeedsUnlock, Denied, Failed }

public sealed record EnterResult(EnterStatus Status, string? Message)
{
    public static readonly EnterResult Ok = new(EnterStatus.Ok, null);
    public static EnterResult Fail(string msg) => new(EnterStatus.Failed, msg);
    public bool Waiting => Status is EnterStatus.NeedsTrust or EnterStatus.NeedsUnlock;
}

public sealed class RecoveryInfo
{
    public ulong Ecid;
    public uint Cpid;
    public uint Bdid;
    public string Serial = "";
    public DeviceMode Mode;
    public string? ProductType;
    public string? DisplayName;
}

/// <summary>Packar upp de inbäddade libimobiledevice-DLL:erna och laddar dem.</summary>
internal static class NativeLoader
{
    private const string Version = "1.3.17";

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string path);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string path);

    public static string Load()
    {
        var asm = typeof(NativeLoader).Assembly;
        var resources = asm.GetManifestResourceNames().Where(n => n.StartsWith("native/")).ToList();
        if (resources.Count == 0) throw new InvalidOperationException("Inga inbäddade bibliotek hittades i programmet.");

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iDeviceRecovery", "native-" + Version);
        try { Extract(asm, resources, dir); }
        catch
        {
            dir = Path.Combine(Path.GetTempPath(), "iDeviceRecovery-native-" + Version);
            Extract(asm, resources, dir);
        }

        SetDllDirectory(dir);
        foreach (var lib in new[] { "plist.dll", "usbmuxd.dll", "imobiledevice.dll", "irecovery.dll" })
        {
            if (LoadLibrary(Path.Combine(dir, lib)) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Kunde inte ladda {lib}");
        }
        try { NativeLibraries.Load(dir); } catch { /* biblioteken är redan laddade ovan */ }
        return dir;
    }

    private static void Extract(System.Reflection.Assembly asm, List<string> resources, string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var res in resources)
        {
            var target = Path.Combine(dir, res["native/".Length..]);
            using var src = asm.GetManifestResourceStream(res)!;
            if (File.Exists(target) && new FileInfo(target).Length == src.Length) continue;
            var tmp = target + ".tmp";
            using (var dst = File.Create(tmp)) src.CopyTo(dst);
            File.Move(tmp, target, overwrite: true);
        }
    }
}

/// <summary>All kommunikation med telefonerna (libimobiledevice + libirecovery).</summary>
public sealed class DeviceService : IDisposable
{
    private readonly IiDeviceApi _idevice;
    private readonly ILockdownApi _lockdown;
    private readonly IPlistApi _plist;
    private readonly IRecoveryApi _recovery;
    private readonly object _gate = new();
    private readonly Dictionary<(uint cpid, uint bdid), (string type, string display)> _irecvDevices = new();

    // Native callbacks hålls vid liv här så att GC inte samlar in dem.
    private iDeviceEventCallBack? _usbCb;
    private RecoveryDeviceEventCallBack? _recCb;
    private RecoveryDeviceEventContextHandle? _recCtx;

    public event Action<string>? NormalAdded;
    public event Action<string>? NormalRemoved;
    public event Action<RecoveryInfo>? RecoveryAdded;
    public event Action<RecoveryInfo>? RecoveryRemoved;

    public string NativeDir { get; }

    public DeviceService()
    {
        NativeDir = NativeLoader.Load();
        var lib = LibiMobileDevice.Instance;
        _idevice = lib.iDevice;
        _lockdown = lib.Lockdown;
        _plist = lib.Plist;
        _recovery = lib.Recovery;
        LoadIrecvDeviceTable();
    }

    public List<string> Start()
    {
        var warnings = new List<string>();

        _usbCb = (ref iDeviceEvent ev, IntPtr _) =>
        {
            if (ev.conn_type != iDeviceConnectionType.Usbmuxd) return; // ignorera Wi-Fi-synk
            var udid = Marshal.PtrToStringAnsi(ev.udid);
            if (string.IsNullOrEmpty(udid)) return;
            if (ev.@event == iDeviceEventType.DeviceAdd) NormalAdded?.Invoke(udid);
            else if (ev.@event == iDeviceEventType.DeviceRemove) NormalRemoved?.Invoke(udid);
        };
        var err = _idevice.idevice_event_subscribe(_usbCb, IntPtr.Zero);
        if (err != iDeviceError.Success) warnings.Add(Loc.F("ErrWatchUsb", err));

        _recCb = (ref RecoveryDeviceEvent ev, IntPtr _) =>
        {
            var info = new RecoveryInfo { Mode = ToMode(ev.mode) };
            if (ev.device_info != IntPtr.Zero)
                FillInfo(info, Marshal.PtrToStructure<RecoveryDeviceInfo>(ev.device_info));
            if (ev.type == RecoveryDeviceEventType.DeviceAdd) RecoveryAdded?.Invoke(info);
            else if (ev.type == RecoveryDeviceEventType.DeviceRemove) RecoveryRemoved?.Invoke(info);
        };
        var rerr = _recovery.irecv_device_event_subscribe(out _recCtx, _recCb, IntPtr.Zero);
        if (rerr != RecoveryError.Success) warnings.Add(Loc.F("ErrWatchRecovery", rerr));

        return warnings;
    }

    // ------------------------------------------------------------ normalt läge

    public List<string> ListNormal()
    {
        int count = 0;
        lock (_gate)
        {
            if (_idevice.idevice_get_device_list(out var udids, ref count) != iDeviceError.Success) return new();
            return udids.ToList();
        }
    }

    public NormalInfo ReadNormalInfo(string udid)
    {
        var info = new NormalInfo();
        lock (_gate)
        {
            if (_idevice.idevice_new(out var device, udid) != iDeviceError.Success) return info;
            using (device)
            {
                if (_lockdown.lockdownd_client_new(device, out var client, "iDeviceRecovery") != LockdownError.Success)
                    return info;
                using (client)
                {
                    info.ProductType = GetString(client, "ProductType") ?? "";
                    info.Version = GetString(client, "ProductVersion") ?? "";
                    info.DeviceName = GetString(client, "DeviceName") ?? "";
                    info.Ecid = GetUInt(client, "UniqueChipID");
                }
            }
        }
        if (info.Ecid == 0) info.Ecid = EcidFromUdid(udid);
        return info;
    }

    /// <summary>
    /// Skickar telefonen till Recovery. Äldre iOS accepterar kommandot utan parning; nyare iOS
    /// (t.ex. 16+) svarar SessionInactive och kräver en betrodd session ("Lita på datorn").
    /// </summary>
    public EnterResult EnterRecovery(string udid)
    {
        lock (_gate)
        {
            var err = _idevice.idevice_new(out var device, udid);
            if (err != iDeviceError.Success) return EnterResult.Fail(Loc.F("ErrNotFound", err));
            using (device)
            {
                // 1) Utan parning – fungerar på äldre iOS och kräver ingen upplåsning.
                var lerr = _lockdown.lockdownd_client_new(device, out var client, "iDeviceRecovery");
                if (lerr == LockdownError.Success)
                {
                    using (client)
                    {
                        lerr = _lockdown.lockdownd_enter_recovery(client);
                        if (lerr == LockdownError.Success) return EnterResult.Ok;
                    }
                }

                // 2) Med betrodd session. Finns ingen parning startar biblioteket "Lita på datorn"-dialogen.
                lerr = _lockdown.lockdownd_client_new_with_handshake(device, out var trusted, "iDeviceRecovery");
                using (trusted)
                {
                    switch (lerr)
                    {
                        case LockdownError.Success:
                            lerr = _lockdown.lockdownd_enter_recovery(trusted);
                            return lerr == LockdownError.Success ? EnterResult.Ok : EnterResult.Fail(Loc.F("ErrEnterRecovery", lerr));
                        case LockdownError.PasswordProtected:
                            return new EnterResult(EnterStatus.NeedsUnlock, Loc.T("StNeedsUnlock"));
                        case LockdownError.PairingDialogResponsePending:
                        case LockdownError.MissingPairRecord:
                        case LockdownError.InvalidHostId:
                        case LockdownError.InvalidPairRecord:
                            return new EnterResult(EnterStatus.NeedsTrust, Loc.T("StNeedsTrust"));
                        case LockdownError.UserDeniedPairing:
                            return new EnterResult(EnterStatus.Denied, Loc.T("StTrustDenied"));
                        default:
                            return EnterResult.Fail(Loc.F("ErrEnterRecovery", lerr));
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ recovery / DFU

    public RecoveryInfo? ScanRecovery()
    {
        lock (_gate)
        {
            if (_recovery.irecv_open_with_ecid(out var client, 0) != RecoveryError.Success) return null;
            using (client)
            {
                int mode = 0;
                _recovery.irecv_get_mode(client, ref mode);
                var info = new RecoveryInfo { Mode = ToMode((RecoveryMode)mode) };
                var ptr = _recovery.irecv_get_device_info(client);
                if (ptr != IntPtr.Zero) FillInfo(info, Marshal.PtrToStructure<RecoveryDeviceInfo>(ptr));
                return info;
            }
        }
    }

    /// <summary>Startar om en telefon i Recovery till normalt läge.</summary>
    public string? ExitRecovery(ulong ecid)
    {
        lock (_gate)
        {
            var err = _recovery.irecv_open_with_ecid_and_attempts(out var client, ecid, 5);
            if (err != RecoveryError.Success) return Loc.F("ErrNoRecovery", err);
            using (client)
            {
                int mode = 0;
                _recovery.irecv_get_mode(client, ref mode);
                if ((RecoveryMode)mode == RecoveryMode.KDfuMode) return Loc.T("ErrInDfu");
                _recovery.irecv_setenv(client, "auto-boot", "true");
                _recovery.irecv_saveenv(client);
                var r = _recovery.irecv_reboot(client);
                return r == RecoveryError.Success ? null : Loc.F("ErrReboot", r);
            }
        }
    }

    // ------------------------------------------------------------ drivrutin

    /// <summary>0 = OK, 1 = installerad men svarar inte, 2 = saknas.</summary>
    public static int CheckAppleService()
    {
        try
        {
            using var tcp = new TcpClient();
            if (tcp.ConnectAsync("127.0.0.1", 27015).Wait(400) && tcp.Connected) return 0;
        }
        catch { }

        if (FindAppleDevicesPackage() != null) return 1;

        try
        {
            using var svcs = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            var names = svcs?.GetSubKeyNames() ?? Array.Empty<string>();
            if (names.Any(n => n.Equals("Apple Mobile Device Service", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("AppleMobileDevice", StringComparison.OrdinalIgnoreCase)
                            || n.Equals("usbaapl64", StringComparison.OrdinalIgnoreCase)))
                return 1;
        }
        catch { }
        return 2;
    }

    /// <summary>
    /// Hittar Microsoft Store-appen "Apple Devices". Den har ingen Windows-tjänst – USB-delen
    /// (AppleMobileDeviceProcess) startas av en startuppgift vid inloggning.
    /// </summary>
    public static (string family, string root)? FindAppleDevicesPackage()
    {
        try
        {
            using var repo = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (repo == null) return null;
            foreach (var name in repo.GetSubKeyNames())
            {
                if (!name.StartsWith("AppleInc.AppleDevices_", StringComparison.OrdinalIgnoreCase)) continue;
                using var k = repo.OpenSubKey(name);
                var root = k?.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, "AppleMobileDeviceLauncher.exe"))) continue;
                var publisherId = name[(name.LastIndexOf('_') + 1)..];
                return ("AppleInc.AppleDevices_" + publisherId, root);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Startar Apple Devices USB-del (AppleMobileDeviceLauncher) i paketets kontext.</summary>
    public static bool StartAppleDevicesService()
    {
        var pkg = FindAppleDevicesPackage();
        if (pkg == null) return false;
        var (family, root) = pkg.Value;
        var exe = Path.Combine(root, "AppleMobileDeviceLauncher.exe").Replace("'", "''");
        var cmd = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{family}' -AppId App -Command '{exe}'";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{cmd}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(20000);
        }
        catch { return false; }

        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(500);
            try
            {
                using var tcp = new TcpClient();
                if (tcp.ConnectAsync("127.0.0.1", 27015).Wait(300) && tcp.Connected) return true;
            }
            catch { }
        }
        return false;
    }

    // ------------------------------------------------------------ hjälpare

    private string? GetString(LockdownClientHandle client, string key)
    {
        if (_lockdown.lockdownd_get_value(client, null!, key, out var node) != LockdownError.Success) return null;
        using (node)
        {
            if (node.IsInvalid) return null;
            _plist.plist_get_string_val(node, out var s);
            return s;
        }
    }

    private ulong GetUInt(LockdownClientHandle client, string key)
    {
        if (_lockdown.lockdownd_get_value(client, null!, key, out var node) != LockdownError.Success) return 0;
        using (node)
        {
            if (node.IsInvalid) return 0;
            ulong v = 0;
            _plist.plist_get_uint_val(node, ref v);
            return v;
        }
    }

    /// <summary>Nya UDID:er har formatet CPID-ECID, t.ex. 00008110-001A2B3C4D5E802E.</summary>
    public static ulong EcidFromUdid(string udid)
    {
        if (udid.Length == 25 && udid[8] == '-')
        {
            try { return Convert.ToUInt64(udid[9..], 16); } catch { }
        }
        return 0;
    }

    private void FillInfo(RecoveryInfo info, RecoveryDeviceInfo di)
    {
        info.Ecid = di.ecid;
        info.Cpid = di.cpid;
        info.Bdid = di.bdid;
        info.Serial = di.srnm != IntPtr.Zero ? Marshal.PtrToStringAnsi(di.srnm) ?? "" : "";
        if (_irecvDevices.TryGetValue((di.cpid, di.bdid), out var d))
        {
            info.ProductType = d.type;
            info.DisplayName = d.display;
        }
    }

    private void LoadIrecvDeviceTable()
    {
        try
        {
            var handle = _recovery.irecv_devices_get_all();
            var ptr = handle.DangerousGetHandle();
            handle.SetHandleAsInvalid(); // statisk tabell i biblioteket – får inte frigöras
            if (ptr == IntPtr.Zero) return;
            int size = Marshal.SizeOf<RecoveryDevice>();
            for (int i = 0; i < 1000; i++)
            {
                var dev = Marshal.PtrToStructure<RecoveryDevice>(ptr + i * size);
                if (dev.product_type == IntPtr.Zero) break;
                var type = Marshal.PtrToStringAnsi(dev.product_type) ?? "";
                var disp = dev.display_name != IntPtr.Zero ? Marshal.PtrToStringAnsi(dev.display_name) ?? "" : "";
                _irecvDevices.TryAdd((dev.chip_id, dev.board_id), (type, disp));
            }
        }
        catch { /* bara för snyggare namn */ }
    }

    private static DeviceMode ToMode(RecoveryMode m) => m switch
    {
        RecoveryMode.KDfuMode => DeviceMode.Dfu,
        RecoveryMode.KWtfMode => DeviceMode.Wtf,
        _ => DeviceMode.Recovery
    };

    public void Dispose()
    {
        try { _idevice.idevice_event_unsubscribe(); } catch { }
        try { _recCtx?.Dispose(); } catch { }
    }
}
