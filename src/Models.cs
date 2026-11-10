using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace iDeviceRecovery;

public enum DeviceMode { Normal, Recovery, Dfu, Wtf }

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DeviceItem : Observable
{
    public required string Key { get; init; }
    public string? Udid { get; init; }
    public ulong Ecid { get; set; }

    private string _name = "iPhone";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string _detail = "";
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    private string _status = "";
    public string Status { get => _status; set { Set(ref _status, value); Raise(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrEmpty(_status);

    private bool _busy;
    public bool Busy { get => _busy; set { Set(ref _busy, value); Raise(nameof(ActionEnabled)); } }

    private DeviceMode _mode;
    public DeviceMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            foreach (var p in new[] { nameof(ModeLabel), nameof(ModeBrush), nameof(ModeSoftBrush), nameof(ActionText), nameof(HasAction), nameof(ActionEnabled) })
                Raise(p);
        }
    }

    public string ModeLabel => Mode switch
    {
        DeviceMode.Normal => Loc.T("ModeNormal"),
        DeviceMode.Recovery => "RECOVERY",
        DeviceMode.Dfu => "DFU",
        _ => "WTF"
    };

    public Brush ModeBrush => Palette.Mode(Mode);
    public Brush ModeSoftBrush => Palette.ModeSoft(Mode);

    public string ActionText => Mode switch
    {
        DeviceMode.Normal => Loc.T("ActToRecovery"),
        DeviceMode.Recovery => Loc.T("ActRestartNormal"),
        _ => ""
    };

    public void RefreshTexts() { Raise(nameof(ModeLabel)); Raise(nameof(ActionText)); }

    public bool HasAction => Mode is DeviceMode.Normal or DeviceMode.Recovery;
    public bool ActionEnabled => HasAction && !Busy;
}

public sealed class StepItem : Observable
{
    public required int Number { get; init; }
    public required string Text { get; init; }
    public required int Seconds { get; init; }

    private int _state; // 0 = väntar, 1 = aktiv, 2 = klar
    public int State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(DotBrush)); Raise(nameof(DotText)); Raise(nameof(TextBrush)); Raise(nameof(DotForeground));
        }
    }

    public Brush DotBrush => State switch { 1 => Palette.Accent, 2 => Palette.Green, _ => Palette.Surface3 };
    public Brush DotForeground => State == 0 ? Palette.Muted : Brushes.White;
    public string DotText => State == 2 ? "✓" : Number.ToString();
    public Brush TextBrush => State == 1 ? Palette.Text : Palette.Muted;
    public string Duration => Seconds <= 1 ? Loc.T("Quick") : $"{Seconds} s";
}

public sealed class LogEntry
{
    public required string Time { get; init; }
    public required string Text { get; init; }
    public required Brush Brush { get; init; }
}

public static class Palette
{
    private static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly Brush Accent = B("#4F8CFF");
    public static readonly Brush Green = B("#2FBF71");
    public static readonly Brush Amber = B("#F0A431");
    public static readonly Brush Purple = B("#B07CFF");
    public static readonly Brush Red = B("#F2555A");
    public static readonly Brush Text = B("#E8EAF0");
    public static readonly Brush Muted = B("#8C93A6");
    public static readonly Brush Surface3 = B("#272C36");

    public static Brush Mode(DeviceMode m) => m switch
    {
        DeviceMode.Normal => Accent,
        DeviceMode.Recovery => Amber,
        DeviceMode.Dfu => Purple,
        _ => Red
    };

    public static Brush ModeSoft(DeviceMode m) => m switch
    {
        DeviceMode.Normal => B("#224F8CFF"),
        DeviceMode.Recovery => B("#22F0A431"),
        DeviceMode.Dfu => B("#22B07CFF"),
        _ => B("#22F2555A")
    };
}

/// <summary>Modellnamn och DFU-knappfamilj.</summary>
public static class AppleModels
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iPhone8,1"] = "iPhone 6s", ["iPhone8,2"] = "iPhone 6s Plus", ["iPhone8,4"] = "iPhone SE",
        ["iPhone9,1"] = "iPhone 7", ["iPhone9,3"] = "iPhone 7", ["iPhone9,2"] = "iPhone 7 Plus", ["iPhone9,4"] = "iPhone 7 Plus",
        ["iPhone10,1"] = "iPhone 8", ["iPhone10,4"] = "iPhone 8", ["iPhone10,2"] = "iPhone 8 Plus", ["iPhone10,5"] = "iPhone 8 Plus",
        ["iPhone10,3"] = "iPhone X", ["iPhone10,6"] = "iPhone X",
        ["iPhone11,2"] = "iPhone XS", ["iPhone11,4"] = "iPhone XS Max", ["iPhone11,6"] = "iPhone XS Max", ["iPhone11,8"] = "iPhone XR",
        ["iPhone12,1"] = "iPhone 11", ["iPhone12,3"] = "iPhone 11 Pro", ["iPhone12,5"] = "iPhone 11 Pro Max", ["iPhone12,8"] = "iPhone SE (2nd gen)",
        ["iPhone13,1"] = "iPhone 12 mini", ["iPhone13,2"] = "iPhone 12", ["iPhone13,3"] = "iPhone 12 Pro", ["iPhone13,4"] = "iPhone 12 Pro Max",
        ["iPhone14,4"] = "iPhone 13 mini", ["iPhone14,5"] = "iPhone 13", ["iPhone14,2"] = "iPhone 13 Pro", ["iPhone14,3"] = "iPhone 13 Pro Max",
        ["iPhone14,6"] = "iPhone SE (3rd gen)",
        ["iPhone14,7"] = "iPhone 14", ["iPhone14,8"] = "iPhone 14 Plus", ["iPhone15,2"] = "iPhone 14 Pro", ["iPhone15,3"] = "iPhone 14 Pro Max",
        ["iPhone15,4"] = "iPhone 15", ["iPhone15,5"] = "iPhone 15 Plus", ["iPhone16,1"] = "iPhone 15 Pro", ["iPhone16,2"] = "iPhone 15 Pro Max",
        ["iPhone17,3"] = "iPhone 16", ["iPhone17,4"] = "iPhone 16 Plus", ["iPhone17,1"] = "iPhone 16 Pro", ["iPhone17,2"] = "iPhone 16 Pro Max",
        ["iPhone17,5"] = "iPhone 16e",
        ["iPhone18,3"] = "iPhone 17", ["iPhone18,1"] = "iPhone 17 Pro", ["iPhone18,2"] = "iPhone 17 Pro Max", ["iPhone18,4"] = "iPhone Air",
    };

    private static readonly Dictionary<uint, string> Chips = new()
    {
        [0x8000] = "A9", [0x8003] = "A9", [0x8010] = "A10", [0x8015] = "A11", [0x8020] = "A12", [0x8030] = "A13",
        [0x8101] = "A14", [0x8110] = "A15", [0x8120] = "A16", [0x8130] = "A17 Pro", [0x8140] = "A18", [0x8150] = "A18 Pro",
        [0x8160] = "A19", [0x8170] = "A19 Pro",
    };

    public static string FriendlyName(string? productType, string? fallbackDisplay = null)
    {
        if (!string.IsNullOrEmpty(productType) && Names.TryGetValue(productType, out var n)) return n;
        if (!string.IsNullOrEmpty(fallbackDisplay)) return fallbackDisplay;
        return string.IsNullOrEmpty(productType) ? "iPhone" : productType;
    }

    public static string Chip(uint cpid) => Chips.TryGetValue(cpid, out var c) ? c : $"0x{cpid:X4}";

    /// <summary>0 = iPhone 8 och senare, 1 = iPhone 7, 2 = hemknapp (6s och äldre).</summary>
    public static int DfuFamily(string? productType)
    {
        if (string.IsNullOrEmpty(productType) || !productType.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase)) return 0;
        var major = productType[6..].Split(',')[0];
        if (!int.TryParse(major, out var m)) return 0;
        return m >= 10 ? 0 : m == 9 ? 1 : 2;
    }

    public static int DfuFamilyFromChip(uint cpid) => cpid switch
    {
        0x8010 => 1,
        0x8000 or 0x8003 or 0x7000 or 0x7001 or 0x8960 => 2,
        _ => 0
    };
}
