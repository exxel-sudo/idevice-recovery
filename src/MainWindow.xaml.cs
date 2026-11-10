using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static iDeviceRecovery.Loc;

namespace iDeviceRecovery;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DeviceItem> _devices = new();
    private readonly ObservableCollection<LogEntry> _log = new();
    private readonly ObservableCollection<StepItem> _steps = new();
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);
    // Telefoner som användaren tagit ut ur Recovery – skickas inte tillbaka automatiskt.
    private readonly HashSet<ulong> _keepNormal = new();
    private readonly Dictionary<ulong, string> _ecidNames = new();
    private readonly DispatcherTimer _driverTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private DeviceService? _svc;
    private int _sent;
    private int _driverState = -1;
    private bool _startingApple;
    private bool _triedStartApple;

    // DFU-guide
    private CancellationTokenSource? _dfuCts;
    private bool _dfuRunning;
    private bool _dfuEverRun;
    private int _dfuResult; // 0 inget, 1 DFU, 2 Recovery

    public MainWindow()
    {
        InitializeComponent();
        DeviceList.ItemsSource = _devices;
        LogList.ItemsSource = _log;
        StepsList.ItemsSource = _steps;
        _devices.CollectionChanged += (_, _) => UpdateEmpty();
        Family0.Checked += (_, _) => BuildSteps();
        Family1.Checked += (_, _) => BuildSteps();
        Family2.Checked += (_, _) => BuildSteps();

        var v = typeof(MainWindow).Assembly.GetName().Version;
        if (v != null) VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";

        // Anpassa till små skärmar / hög skalning
        var wa = SystemParameters.WorkArea;
        Height = Math.Max(MinHeight, Math.Min(Height, wa.Height - 40));
        Width = Math.Max(MinWidth, Math.Min(Width, wa.Width - 40));

        ApplyLanguage();
        LanguageChanged += ApplyLanguage;

        Loaded += OnLoaded;
        Closed += (_, _) => { LanguageChanged -= ApplyLanguage; _dfuCts?.Cancel(); _svc?.Dispose(); };
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };
    }

    // =============================================================== språk

    private void LangSv_Click(object sender, RoutedEventArgs e) => SetLanguage(false);
    private void LangEn_Click(object sender, RoutedEventArgs e) => SetLanguage(true);

    private void SetLanguage(bool english)
    {
        if (Instance.English == english) return;
        Instance.SetEnglish(english);
        Log(T("LogLanguage"), Palette.Muted);
    }

    /// <summary>Uppdaterar texter som sätts från kod (XAML-bindningarna uppdateras själva).</summary>
    private void ApplyLanguage()
    {
        LangSv.Background = Instance.English ? Brushes.Transparent : Palette.Accent;
        LangEn.Background = Instance.English ? Palette.Accent : Brushes.Transparent;
        LangSv.Foreground = Instance.English ? Palette.Muted : Brushes.White;
        LangEn.Foreground = Instance.English ? Brushes.White : Palette.Muted;
        RenderDriver();
        UpdateSentCounter();
        AutoLabel.Text = AutoToggle.IsChecked == true ? T("On") : T("Off");
        foreach (var d in _devices) d.RefreshTexts();
        if (!_dfuRunning)
        {
            BuildSteps();
            SetDfuIdle(T("IdleTitle"), T("IdleText"));
            ResultBanner.Visibility = Visibility.Collapsed;
            DfuButton.Content = _dfuEverRun ? T("StartAgain") : T("Start");
        }
        else DfuButton.Content = T("Cancel");
    }

    private void SetDfuIdle(string title, string text)
    {
        SetRing(0);
        CountText.Text = "–";
        CountUnit.Text = T("Ready");
        StepTitle.Text = title;
        StepText.Text = text;
    }

    // =============================================================== start

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyWin11Corners();
        Log(T("LogStarting"), Palette.Muted);

        try
        {
            _svc = await Task.Run(() => new DeviceService());
            Log(T("LogLibsLoaded"), Palette.Muted);
        }
        catch (Exception ex)
        {
            Log(F("LogLibsFailed", ex.Message), Palette.Red);
            ShowBanner(T("BannerLibsFailed"), Palette.Red);
            return;
        }

        _svc.NormalAdded += udid => Ui(() => OnNormalAdded(udid));
        _svc.NormalRemoved += udid => Ui(() => Remove("u:" + udid));
        _svc.RecoveryAdded += info => Ui(() => OnRecoveryAdded(info));
        _svc.RecoveryRemoved += info => Ui(() => ScheduleRemove("r:" + info.Ecid.ToString("X")));

        foreach (var w in _svc.Start()) Log(w, Palette.Amber);

        _driverTimer.Tick += (_, _) => _ = CheckDriverAsync();
        _driverTimer.Start();
        await CheckDriverAsync();
        Log(T("LogReady"), Palette.Text);
    }

    private void Ui(Action a) => Dispatcher.BeginInvoke(a);

    // =============================================================== drivrutin

    private async Task CheckDriverAsync()
    {
        if (_startingApple) return;
        int s = await Task.Run(DeviceService.CheckAppleService);
        if (s != _driverState)
        {
            _driverState = s;
            RenderDriver();
            if (s == 0) Log(T("DriverConnected"), Palette.Green);
            else Log(DriverText.Text, s == 1 ? Palette.Amber : Palette.Red);
        }
        // Apple Devices (Store) finns men USB-delen körs inte – starta den automatiskt en gång.
        if (s == 1 && !_triedStartApple && DeviceService.FindAppleDevicesPackage() != null)
            await StartAppleAsync();
    }

    private async Task StartAppleAsync()
    {
        if (_startingApple) return;
        _startingApple = true;
        _triedStartApple = true;
        DriverText.Text = T("DriverStarting");
        Log(T("LogStartingApple"), Palette.Muted);
        bool ok = await Task.Run(DeviceService.StartAppleDevicesService);
        _startingApple = false;
        if (!ok) Log(T("LogStartAppleFailed"), Palette.Amber);
        _driverState = -2; // tvinga omritning
        await CheckDriverAsync();
    }

    private void RenderDriver()
    {
        (Brush color, string text, string bg) = _driverState switch
        {
            -1 => (Palette.Muted, T("DriverChecking"), "#14FFFFFF"),
            0 => (Palette.Green, T("DriverOk"), "#1A2FBF71"),
            1 => (Palette.Amber, DeviceService.FindAppleDevicesPackage() != null ? T("DriverNotRunning") : T("DriverNotResponding"), "#1AF0A431"),
            _ => (Palette.Red, T("DriverMissing"), "#1AF2555A"),
        };
        DriverDot.Fill = color;
        DriverText.Foreground = color;
        DriverText.Text = text;
        DriverPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        DriverPill.ToolTip = _driverState == 2 ? T("DriverTip") : null;
    }

    private void DriverPill_Click(object sender, RoutedEventArgs e)
    {
        if (_driverState is 0 or -1 || _startingApple) return;
        if (_driverState == 1 && DeviceService.FindAppleDevicesPackage() != null)
        {
            _ = StartAppleAsync();
            return;
        }
        try { Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NP83LWLPZ9K") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo("https://apps.microsoft.com/detail/9NP83LWLPZ9K") { UseShellExecute = true }); }
    }

    // =============================================================== normalt läge

    private void OnNormalAdded(string udid)
    {
        var key = "u:" + udid;
        if (_devices.Any(d => d.Key == key)) return;

        var item = new DeviceItem { Key = key, Udid = udid, Mode = DeviceMode.Normal, Name = "iPhone", Detail = T("ReadingInfo") };
        item.Ecid = DeviceService.EcidFromUdid(udid);
        _devices.Insert(0, item);

        bool auto = AutoToggle.IsChecked == true && !_dfuRunning;
        bool once = OnceToggle.IsChecked == true;
        if (auto) item.Busy = true;

        _ = Task.Run(() =>
        {
            var info = _svc!.ReadNormalInfo(udid);
            Ui(() =>
            {
                var name = AppleModels.FriendlyName(info.ProductType);
                item.Name = string.IsNullOrEmpty(info.DeviceName) ? name : $"{name}  ·  {info.DeviceName}";
                item.Ecid = info.Ecid;
                item.Detail = $"iOS {(info.Version.Length > 0 ? info.Version : "?")}   ·   UDID {udid}";
                if (info.Ecid != 0) _ecidNames[info.Ecid] = name;
                Log(F("LogConnected", name, info.Version), Palette.Text);
                if (!_dfuRunning && info.ProductType.Length > 0) SelectFamily(AppleModels.DfuFamily(info.ProductType));
            });

            if (!auto) return;
            string? skipReason = Dispatcher.Invoke(() =>
                _keepNormal.Contains(info.Ecid) ? T("StSkipKeep")
                : once && _handled.Contains(udid) ? T("StSkipOnce")
                : null);
            if (skipReason != null)
            {
                Ui(() => { item.Busy = false; item.Status = skipReason; });
                return;
            }
            Ui(() => item.Status = T("StSending"));
            var res = EnterWithRetry(item);
            Ui(() => FinishEnterRecovery(item, res, automatic: true));
        });
    }

    /// <summary>
    /// Körs i bakgrunden. Kräver telefonen "Lita på datorn" (iOS 16+ med lösenkod) väntar vi
    /// tills användaren låst upp och tryckt Lita på – max 2 minuter eller tills telefonen kopplas ur.
    /// </summary>
    private EnterResult EnterWithRetry(DeviceItem item)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        EnterStatus? lastLogged = null;
        while (true)
        {
            var res = _svc!.EnterRecovery(item.Udid!);
            if (!res.Waiting) return res;

            Ui(() =>
            {
                item.Status = res.Message ?? "";
                if (lastLogged != res.Status) Log($"{item.Name}: {res.Message}", Palette.Amber);
            });
            lastLogged = res.Status;

            Thread.Sleep(2000);
            bool stillThere = Dispatcher.Invoke(() => _devices.Contains(item));
            if (!stillThere) return EnterResult.Fail(T("StGaveUp"));
            if (DateTime.UtcNow > deadline) return EnterResult.Fail(T("StTrustTimeout"));
        }
    }

    private void FinishEnterRecovery(DeviceItem item, EnterResult res, bool automatic)
    {
        item.Busy = false;
        var err = res.Status == EnterStatus.Ok ? null : res.Message;
        if (err == null)
        {
            _handled.Add(item.Udid!);
            _sent++;
            UpdateSentCounter();
            item.Status = T("StSent");
            Log(F("LogToRecovery", item.Name) + (automatic ? T("LogAuto") : ""), Palette.Amber);
        }
        else
        {
            item.Status = err;
            Log($"{item.Name}: {err}", Palette.Red);
        }
    }

    private void UpdateSentCounter() =>
        SentCounter.Text = _sent == 1 ? T("SentOne") : F("SentMany", _sent);

    // =============================================================== recovery / DFU

    private void OnRecoveryAdded(RecoveryInfo info)
    {
        if (_dfuRunning)
        {
            if (info.Mode == DeviceMode.Dfu) _dfuResult = 1;
            else if (info.Mode == DeviceMode.Recovery && _dfuResult == 0) _dfuResult = 2;
        }

        var key = "r:" + info.Ecid.ToString("X");
        CancelRemove(key);
        var item = _devices.FirstOrDefault(d => d.Key == key);
        bool isNew = item == null;
        if (item == null)
        {
            item = new DeviceItem { Key = key, Ecid = info.Ecid };
            _devices.Insert(0, item);
        }
        else if (item.Mode == info.Mode) return; // samma enhet i samma läge – bara ett USB-glapp
        item.Mode = info.Mode;

        string name = _ecidNames.TryGetValue(info.Ecid, out var n) ? n
            : info.ProductType != null ? AppleModels.FriendlyName(info.ProductType, info.DisplayName)
            : $"iPhone ({AppleModels.Chip(info.Cpid)})";
        item.Name = name;
        item.Detail = $"ECID 0x{info.Ecid:X}   ·   {AppleModels.Chip(info.Cpid)}" + (info.Serial.Length > 0 ? $"   ·   SN {info.Serial}" : "");
        if (isNew || !item.Busy)
            item.Status = info.Mode switch
            {
                DeviceMode.Dfu => T("StDfuHint"),
                DeviceMode.Recovery => T("StRecHint"),
                _ => ""
            };

        var modeText = info.Mode switch { DeviceMode.Dfu => T("ModeDfuName"), DeviceMode.Recovery => T("ModeRecName"), _ => T("ModeWtfName") };
        Log(F("LogInMode", name, modeText, info.Ecid), Palette.Mode(info.Mode));

        if (!_dfuRunning && info.Cpid != 0 && info.ProductType == null && !_ecidNames.ContainsKey(info.Ecid))
            SelectFamily(AppleModels.DfuFamilyFromChip(info.Cpid));
    }

    // Recovery/DFU-enheter kan "glappa" (t.ex. när iTunes samtidigt läser telefonen).
    // Ta därför bort dem först om de varit borta i 3 sekunder.
    private readonly Dictionary<string, DispatcherTimer> _pendingRemove = new();

    private void ScheduleRemove(string key)
    {
        CancelRemove(key);
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            _pendingRemove.Remove(key);
            Remove(key);
        };
        _pendingRemove[key] = t;
        t.Start();
    }

    private void CancelRemove(string key)
    {
        if (_pendingRemove.Remove(key, out var t)) t.Stop();
    }

    private void Remove(string key)
    {
        var item = _devices.FirstOrDefault(d => d.Key == key);
        if (item == null) return;
        _devices.Remove(item);
        Log(F("LogRemoved", item.Name), Palette.Muted);
    }

    // =============================================================== knappar

    private void DeviceAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceItem item || _svc == null) return;
        item.Busy = true;

        if (item.Mode == DeviceMode.Normal)
        {
            item.Status = T("StSending");
            _ = Task.Run(() =>
            {
                var res = EnterWithRetry(item);
                Ui(() => FinishEnterRecovery(item, res, automatic: false));
            });
        }
        else if (item.Mode == DeviceMode.Recovery)
        {
            item.Status = T("StRestarting");
            var ecid = item.Ecid;
            _ = Task.Run(() =>
            {
                var err = _svc.ExitRecovery(ecid);
                Ui(() =>
                {
                    item.Busy = false;
                    item.Status = err ?? T("StRestarted");
                    if (err == null && ecid != 0) _keepNormal.Add(ecid);
                    Log(err == null ? F("LogLeavesRecovery", item.Name) : $"{item.Name}: {err}", err == null ? Palette.Green : Palette.Red);
                });
            });
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_svc == null) return;
        Log(T("LogScanning"), Palette.Muted);
        await CheckDriverAsync();
        var (normal, rec) = await Task.Run(() => (_svc.ListNormal(), _svc.ScanRecovery()));
        foreach (var u in normal) OnNormalAdded(u);
        if (rec != null) OnRecoveryAdded(rec);
        if (normal.Count == 0 && rec == null) Log(T("LogNoPhones"), Palette.Muted);
    }

    private async void ExitAnyRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (_svc == null) return;
        Log(T("LogLookingRecovery"), Palette.Muted);
        var rec = await Task.Run(() => _svc.ScanRecovery());
        if (rec == null) { Log(T("LogNoRecovery"), Palette.Amber); return; }
        if (rec.Mode == DeviceMode.Dfu) { Log(T("LogDfuForce"), Palette.Amber); return; }
        var err = await Task.Run(() => _svc.ExitRecovery(rec.Ecid));
        if (err == null)
        {
            if (rec.Ecid != 0) _keepNormal.Add(rec.Ecid);
            Log(F("LogRestartingEcid", rec.Ecid), Palette.Green);
        }
        else Log(err, Palette.Red);
    }

    private void AutoToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoLabel == null) return;
        bool on = AutoToggle.IsChecked == true;
        AutoLabel.Text = on ? T("On") : T("Off");
        if (IsLoaded) Log(on ? T("LogAutoOn") : T("LogAutoOff"), Palette.Muted);
    }

    private void CopyUdid_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceItem d && d.Udid != null) Clipboard.SetText(d.Udid);
    }

    private void CopyEcid_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceItem d && d.Ecid != 0) Clipboard.SetText($"0x{d.Ecid:X}");
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(string.Join(Environment.NewLine, _log.Select(l => $"{l.Time} {l.Text}")));

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // =============================================================== DFU-guide

    private int SelectedFamily => Family1.IsChecked == true ? 1 : Family2.IsChecked == true ? 2 : 0;

    private void SelectFamily(int f) => (f switch { 1 => Family1, 2 => Family2, _ => Family0 }).IsChecked = true;

    private static (string key, int secs)[] StepsFor(int family) => family switch
    {
        1 => new[] { ("S_Ready", 3), ("S7_1", 8), ("S7_2", 10) },
        2 => new[] { ("S_Ready", 3), ("S6_1", 8), ("S6_2", 10) },
        _ => new[] { ("S_Ready", 3), ("S8_1", 1), ("S8_2", 1), ("S8_3", 10), ("S8_4", 5), ("S8_5", 10) },
    };

    private void BuildSteps()
    {
        if (_dfuRunning || StepsList == null) return;
        _steps.Clear();
        var steps = StepsFor(SelectedFamily);
        for (int i = 0; i < steps.Length; i++)
            _steps.Add(new StepItem { Number = i + 1, Text = T(steps[i].key), Seconds = steps[i].secs });
    }

    private async void DfuButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dfuRunning) { _dfuCts?.Cancel(); return; }
        await RunDfuGuide();
    }

    private async Task RunDfuGuide()
    {
        BuildSteps();
        _dfuCts = new CancellationTokenSource();
        var ct = _dfuCts.Token;
        _dfuRunning = true;
        _dfuEverRun = true;
        _dfuResult = 0;
        ResultBanner.Visibility = Visibility.Collapsed;
        ModelPanel.Visibility = Visibility.Collapsed;
        StepsScroll.Visibility = Visibility.Visible;
        RingScale.ScaleX = RingScale.ScaleY = 1;
        DfuButton.Content = T("Cancel");
        DfuButton.Background = Palette.Surface3;
        if (AutoToggle.IsChecked == true) Log(T("LogAutoPaused"), Palette.Muted);
        Log(T("LogDfuStarted"), Palette.Purple);

        try
        {
            var sw = new Stopwatch();
            foreach (var step in _steps)
            {
                step.State = 1;
                StepTitle.Text = F("StepOf", step.Number, _steps.Count);
                StepText.Text = step.Text;
                CountUnit.Text = T("Seconds");
                double total = Math.Max(step.Seconds, 1);
                sw.Restart();
                while (sw.Elapsed.TotalSeconds < total)
                {
                    if (_dfuResult == 1) break;
                    double left = total - sw.Elapsed.TotalSeconds;
                    CountText.Text = Math.Ceiling(left).ToString("0");
                    SetRing(left / total);
                    await Task.Delay(33, ct);
                }
                step.State = 2;
                if (_dfuResult == 1) break;
            }

            // Ge drivrutinen en stund att rapportera enheten
            StepTitle.Text = T("Checking");
            StepText.Text = T("KeepHolding");
            CountText.Text = "…";
            CountUnit.Text = "";
            for (int i = 0; i < 40 && _dfuResult == 0; i++)
            {
                SetRing((i % 20) / 20.0);
                await Task.Delay(100, ct);
            }

            SetRing(0);
            switch (_dfuResult)
            {
                case 1:
                    CountText.Text = "✓"; CountUnit.Text = "DFU";
                    StepTitle.Text = T("Done"); StepText.Text = T("ReleaseButtons");
                    ShowBanner(T("ResDfu"), Palette.Purple);
                    Log(T("LogDfuDetected"), Palette.Purple);
                    break;
                case 2:
                    CountText.Text = "!"; CountUnit.Text = "Recovery";
                    StepTitle.Text = T("NotDfu"); StepText.Text = T("EndedInRecovery");
                    ShowBanner(T("ResRecovery"), Palette.Amber);
                    break;
                default:
                    CountText.Text = "×"; CountUnit.Text = "";
                    StepTitle.Text = T("NoDfu"); StepText.Text = T("NoDfuText");
                    ShowBanner(T("ResNone"), Palette.Red);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            SetDfuIdle(T("Cancelled"), T("PressStart"));
            Log(T("LogDfuCancelled"), Palette.Muted);
        }
        finally
        {
            _dfuRunning = false;
            ModelPanel.Visibility = Visibility.Visible;
            StepsScroll.Visibility = Visibility.Collapsed;
            RingScale.ScaleX = RingScale.ScaleY = 0.85;
            DfuButton.Content = T("StartAgain");
            DfuButton.ClearValue(BackgroundProperty);
        }
    }

    private void SetRing(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        if (fraction <= 0.001) { RingArc.Data = null; return; }
        const double r = 62, c = 66;
        double a = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        RingArc.Data = new PathGeometry(new[] { fig });
    }

    private void ShowBanner(string text, Brush color)
    {
        ResultText.Text = text;
        ResultText.Foreground = color;
        var c = ((SolidColorBrush)color).Color;
        ResultBanner.Background = new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
        ResultBanner.Visibility = Visibility.Visible;
    }

    // =============================================================== övrigt

    private void UpdateEmpty()
    {
        EmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeviceCount.Text = _devices.Count.ToString();
    }

    private void Log(string text, Brush brush)
    {
        _log.Add(new LogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Text = text, Brush = brush });
        while (_log.Count > 500) _log.RemoveAt(0);
        LogList.ScrollIntoView(_log[^1]);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyWin11Corners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch { }
    }
}
