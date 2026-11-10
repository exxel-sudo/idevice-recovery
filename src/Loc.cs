using System.ComponentModel;
using System.Globalization;
using System.IO;

namespace iDeviceRecovery;

/// <summary>Enkel språkhantering (svenska / engelska). Bind i XAML med {Binding [Nyckel], Source={x:Static l:Loc.Instance}}.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? LanguageChanged;

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iDeviceRecovery", "lang.txt");

    public bool English { get; private set; }

    private Loc()
    {
        string? saved = null;
        try { if (File.Exists(SettingsFile)) saved = File.ReadAllText(SettingsFile).Trim(); } catch { }
        English = saved != null ? saved == "en" : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "sv";
    }

    public string this[string key] => T(key);

    public static string T(string key) =>
        (Instance.English ? En : Sv).TryGetValue(key, out var v) ? v : key;

    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    public void SetEnglish(bool english)
    {
        if (English == english) return;
        English = english;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, english ? "en" : "sv");
        }
        catch { }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    private static readonly Dictionary<string, string> Sv = new()
    {
        ["LangButton"] = "English",
        ["DriverChecking"] = "Kontrollerar drivrutin…",
        ["DriverOk"] = "Apple-drivrutin OK",
        ["DriverNotResponding"] = "Apple Mobile Device-tjänsten svarar inte",
        ["DriverMissing"] = "Apple-drivrutin saknas – klicka för att installera",
        ["DriverTip"] = "Installera \"Apple Devices\" från Microsoft Store (eller iTunes). Utan den ser Windows inte telefonen.",
        ["DriverConnected"] = "Ansluten till Apple Mobile Device Service.",
        ["DriverStarting"] = "Startar Apple Mobile Device…",
        ["DriverNotRunning"] = "Apple Devices körs inte – klicka för att starta",
        ["LogStartingApple"] = "Apple Devices är installerat men USB-tjänsten körs inte – startar den…",
        ["LogStartAppleFailed"] = "Kunde inte starta Apple Mobile Device automatiskt. Öppna Apple Devices-appen eller logga ut/in en gång.",

        ["AutoTitle"] = "Automatisk Recovery vid inkoppling",
        ["AutoDesc"] = "Varje iPhone som kopplas in i normalt läge skickas direkt till Recovery-läge. Äldre iOS kräver ingenting. På nyare iOS (16+) med lösenkod måste telefonen litas på en gång – programmet väntar då på \"Lita på\".",
        ["Once"] = "Bara en gång per telefon (så att den inte fastnar i en loop)",
        ["On"] = "PÅ",
        ["Off"] = "AV",
        ["SentOne"] = "1 skickad",
        ["SentMany"] = "{0} skickade",

        ["Devices"] = "Anslutna enheter",
        ["ExitAny"] = "Ta ut ur Recovery",
        ["ExitAnyTip"] = "Startar om en telefon som fastnat i Recovery till normalt läge – även om den inte syns i listan",
        ["Rescan"] = "Sök igen",
        ["EmptyTitle"] = "Ingen iPhone ansluten",
        ["EmptyDesc"] = "Koppla in en telefon med USB-kabel – den dyker upp här direkt.",
        ["CopyUdid"] = "Kopiera UDID",
        ["CopyEcid"] = "Kopiera ECID",

        ["ModeNormal"] = "NORMALT",
        ["ActToRecovery"] = "Till Recovery",
        ["ActRestartNormal"] = "Starta om normalt",
        ["ReadingInfo"] = "Läser information…",
        ["StSending"] = "Skickar till Recovery…",
        ["StSent"] = "✓ Skickad – startar om i Recovery…",
        ["StSkipKeep"] = "Startades om från Recovery – skickas inte tillbaka",
        ["StSkipOnce"] = "Redan skickad till Recovery – hoppar över",
        ["StDfuHint"] = "Lämna DFU: tvingad omstart med knapparna",
        ["StRecHint"] = "Fastnat här? Klicka \"Starta om normalt\"",
        ["StRestarting"] = "Startar om till normalt läge…",
        ["StNeedsTrust"] = "Tryck \"Lita på\" på telefonen och ange koden – väntar…",
        ["StNeedsUnlock"] = "Lås upp telefonen (iOS kräver \"Lita på datorn\") – väntar…",
        ["StTrustDenied"] = "\"Lita inte på\" valdes på telefonen. Koppla ur och in igen för att försöka på nytt.",
        ["StTrustTimeout"] = "Gav upp – ingen \"Lita på\" inom 2 minuter.",
        ["StGaveUp"] = "Telefonen kopplades ur.",
        ["StRestarted"] = "✓ Startar om till normalt läge…",

        ["DfuTitle"] = "DFU-guide",
        ["DfuBadge"] = "KNAPPSEKVENS",
        ["DfuDesc"] = "Apple tillåter inte att DFU startas via USB. Guiden räknar tiden åt dig och känner av när telefonen är i DFU.",
        ["Model"] = "MODELL",
        ["Fam0"] = "iPhone 8 och senare",
        ["Fam0Sub"] = "X, XR, XS, 11–17, Air, SE 2 & 3",
        ["Fam1"] = "iPhone 7 / 7 Plus",
        ["Fam1Sub"] = "Sidoknapp + volym ned",
        ["Fam2"] = "iPhone 6s och äldre, SE (1:a gen)",
        ["Fam2Sub"] = "Med hemknapp",
        ["Ready"] = "redo",
        ["Seconds"] = "sekunder",
        ["IdleTitle"] = "VÄLJ MODELL OCH TRYCK STARTA",
        ["IdleText"] = "Koppla in telefonen med USB-kabel först.",
        ["Start"] = "Starta DFU-guiden",
        ["StartAgain"] = "Starta DFU-guiden igen",
        ["Cancel"] = "Avbryt",
        ["StepOf"] = "STEG {0} AV {1}",
        ["Quick"] = "snabbt",
        ["Checking"] = "KONTROLLERAR",
        ["KeepHolding"] = "Fortsätt hålla knappen…",
        ["Done"] = "KLART",
        ["ReleaseButtons"] = "Släpp knapparna.",
        ["ResDfu"] = "Telefonen är i DFU-läge. Skärmen ska vara helt svart. Nu kan du återställa med Apple Devices / iTunes.",
        ["NotDfu"] = "INTE DFU",
        ["EndedInRecovery"] = "Telefonen hamnade i Recovery.",
        ["ResRecovery"] = "Kabel/dator-ikonen syns = Recovery, inte DFU. Sidoknappen hölls troligen för länge. Försök igen.",
        ["NoDfu"] = "INGEN DFU",
        ["NoDfuText"] = "Ingen DFU-enhet upptäcktes.",
        ["ResNone"] = "Visade telefonen Apple-loggan startade den om normalt – börja om. Kontrollera också att Apple-drivrutinen är OK.",
        ["Cancelled"] = "AVBRUTEN",
        ["PressStart"] = "Tryck Starta för att försöka igen.",

        ["S_Ready"] = "Koppla in telefonen och lägg fingrarna på knapparna",
        ["S7_1"] = "Håll in SIDOKNAPPEN + VOLYM NED",
        ["S7_2"] = "Släpp sidoknappen – fortsätt hålla VOLYM NED",
        ["S6_1"] = "Håll in HEMKNAPPEN + SIDO/TOPP-knappen",
        ["S6_2"] = "Släpp sido/topp-knappen – fortsätt hålla HEMKNAPPEN",
        ["S8_1"] = "Tryck snabbt VOLYM UPP och släpp",
        ["S8_2"] = "Tryck snabbt VOLYM NED och släpp",
        ["S8_3"] = "Håll in SIDOKNAPPEN tills skärmen blir svart",
        ["S8_4"] = "Håll kvar sidoknappen och håll även VOLYM NED",
        ["S8_5"] = "Släpp sidoknappen – fortsätt hålla VOLYM NED",

        ["Log"] = "LOGG",
        ["Copy"] = "Kopiera",
        ["Clear"] = "Rensa",
        ["LogStarting"] = "Startar…",
        ["LogLibsLoaded"] = "libimobiledevice + libirecovery laddade.",
        ["LogLibsFailed"] = "Kunde inte ladda biblioteken: {0}",
        ["BannerLibsFailed"] = "Biblioteken kunde inte laddas. Se loggen.",
        ["LogReady"] = "Redo. Koppla in en iPhone.",
        ["LogConnected"] = "Ansluten: {0} (iOS {1})",
        ["LogToRecovery"] = "{0} → Recovery-läge",
        ["LogAuto"] = " (auto)",
        ["LogInMode"] = "{0} är i {1} (ECID 0x{2:X})",
        ["ModeDfuName"] = "DFU-läge",
        ["ModeRecName"] = "Recovery-läge",
        ["ModeWtfName"] = "WTF-läge",
        ["LogRemoved"] = "Frånkopplad / bytte läge: {0}",
        ["LogLeavesRecovery"] = "{0} lämnar Recovery",
        ["LogScanning"] = "Söker efter enheter…",
        ["LogNoPhones"] = "Inga telefoner hittades.",
        ["LogLookingRecovery"] = "Letar efter telefon i Recovery…",
        ["LogNoRecovery"] = "Ingen telefon i Recovery hittades.",
        ["LogDfuForce"] = "Telefonen är i DFU – gör en tvingad omstart med knapparna.",
        ["LogRestartingEcid"] = "Startar om till normalt läge (ECID 0x{0:X}).",
        ["LogAutoOn"] = "Automatisk Recovery: PÅ",
        ["LogAutoOff"] = "Automatisk Recovery: AV",
        ["LogAutoPaused"] = "Automatisk Recovery pausas medan DFU-guiden körs.",
        ["LogDfuStarted"] = "DFU-guide startad.",
        ["LogDfuDetected"] = "DFU-läge upptäckt!",
        ["LogDfuCancelled"] = "DFU-guide avbruten.",
        ["LogLanguage"] = "Språk: svenska",

        ["ErrNotFound"] = "Hittar inte telefonen ({0})",
        ["ErrLockdown"] = "Lockdown-fel ({0})",
        ["ErrEnterRecovery"] = "EnterRecovery misslyckades ({0})",
        ["ErrNoRecovery"] = "Hittar inte telefonen i Recovery ({0})",
        ["ErrInDfu"] = "Telefonen är i DFU – gör en tvingad omstart med knapparna.",
        ["ErrReboot"] = "Omstart misslyckades ({0})",
        ["ErrWatchUsb"] = "Kan inte bevaka USB-enheter ({0}).",
        ["ErrWatchRecovery"] = "Kan inte bevaka Recovery/DFU-enheter ({0}).",
        ["UnexpectedError"] = "oväntat fel",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["LangButton"] = "Svenska",
        ["DriverChecking"] = "Checking driver…",
        ["DriverOk"] = "Apple driver OK",
        ["DriverNotResponding"] = "Apple Mobile Device Service is not responding",
        ["DriverMissing"] = "Apple driver missing – click to install",
        ["DriverTip"] = "Install \"Apple Devices\" from the Microsoft Store (or iTunes). Without it Windows cannot see the phone.",
        ["DriverConnected"] = "Connected to Apple Mobile Device Service.",
        ["DriverStarting"] = "Starting Apple Mobile Device…",
        ["DriverNotRunning"] = "Apple Devices not running – click to start",
        ["LogStartingApple"] = "Apple Devices is installed but its USB service is not running – starting it…",
        ["LogStartAppleFailed"] = "Could not start Apple Mobile Device automatically. Open the Apple Devices app or sign out and back in once.",

        ["AutoTitle"] = "Auto Recovery on connect",
        ["AutoDesc"] = "Every iPhone plugged in while in normal mode is sent straight to Recovery mode. Older iOS needs nothing. On newer iOS (16+) with a passcode the phone must trust this computer once – the app then waits for \"Trust\".",
        ["Once"] = "Only once per phone (prevents a reboot loop)",
        ["On"] = "ON",
        ["Off"] = "OFF",
        ["SentOne"] = "1 sent",
        ["SentMany"] = "{0} sent",

        ["Devices"] = "Connected devices",
        ["ExitAny"] = "Exit Recovery",
        ["ExitAnyTip"] = "Reboots a phone stuck in Recovery into normal mode – even if it is not shown in the list",
        ["Rescan"] = "Rescan",
        ["EmptyTitle"] = "No iPhone connected",
        ["EmptyDesc"] = "Plug in a phone with a USB cable – it shows up here right away.",
        ["CopyUdid"] = "Copy UDID",
        ["CopyEcid"] = "Copy ECID",

        ["ModeNormal"] = "NORMAL",
        ["ActToRecovery"] = "To Recovery",
        ["ActRestartNormal"] = "Reboot to normal",
        ["ReadingInfo"] = "Reading device info…",
        ["StSending"] = "Sending to Recovery…",
        ["StSent"] = "✓ Sent – rebooting into Recovery…",
        ["StSkipKeep"] = "Was rebooted out of Recovery – not sending it back",
        ["StSkipOnce"] = "Already sent to Recovery – skipping",
        ["StDfuHint"] = "To leave DFU: force restart with the buttons",
        ["StRecHint"] = "Stuck here? Click \"Reboot to normal\"",
        ["StRestarting"] = "Rebooting to normal mode…",
        ["StNeedsTrust"] = "Tap \"Trust\" on the phone and enter the passcode – waiting…",
        ["StNeedsUnlock"] = "Unlock the phone (iOS requires \"Trust This Computer\") – waiting…",
        ["StTrustDenied"] = "\"Don't Trust\" was chosen on the phone. Unplug and reconnect to try again.",
        ["StTrustTimeout"] = "Gave up – no \"Trust\" within 2 minutes.",
        ["StGaveUp"] = "The phone was disconnected.",
        ["StRestarted"] = "✓ Rebooting to normal mode…",

        ["DfuTitle"] = "DFU guide",
        ["DfuBadge"] = "BUTTON SEQUENCE",
        ["DfuDesc"] = "Apple does not allow DFU to be triggered over USB. The guide times each step for you and detects when the phone is in DFU.",
        ["Model"] = "MODEL",
        ["Fam0"] = "iPhone 8 and later",
        ["Fam0Sub"] = "X, XR, XS, 11–17, Air, SE 2 & 3",
        ["Fam1"] = "iPhone 7 / 7 Plus",
        ["Fam1Sub"] = "Side button + volume down",
        ["Fam2"] = "iPhone 6s and older, SE (1st gen)",
        ["Fam2Sub"] = "With Home button",
        ["Ready"] = "ready",
        ["Seconds"] = "seconds",
        ["IdleTitle"] = "PICK A MODEL AND PRESS START",
        ["IdleText"] = "Connect the phone with a USB cable first.",
        ["Start"] = "Start DFU guide",
        ["StartAgain"] = "Start DFU guide again",
        ["Cancel"] = "Cancel",
        ["StepOf"] = "STEP {0} OF {1}",
        ["Quick"] = "quick",
        ["Checking"] = "CHECKING",
        ["KeepHolding"] = "Keep holding the button…",
        ["Done"] = "DONE",
        ["ReleaseButtons"] = "Release the buttons.",
        ["ResDfu"] = "The phone is in DFU mode. The screen should be completely black. You can now restore with Apple Devices / iTunes.",
        ["NotDfu"] = "NOT DFU",
        ["EndedInRecovery"] = "The phone ended up in Recovery.",
        ["ResRecovery"] = "A cable/computer icon means Recovery, not DFU. The side button was probably held too long. Try again.",
        ["NoDfu"] = "NO DFU",
        ["NoDfuText"] = "No DFU device detected.",
        ["ResNone"] = "If the Apple logo appeared, the phone restarted normally – start over. Also check that the Apple driver is OK.",
        ["Cancelled"] = "CANCELLED",
        ["PressStart"] = "Press Start to try again.",

        ["S_Ready"] = "Connect the phone and place your fingers on the buttons",
        ["S7_1"] = "Hold the SIDE button + VOLUME DOWN",
        ["S7_2"] = "Release the side button – keep holding VOLUME DOWN",
        ["S6_1"] = "Hold the HOME button + SIDE/TOP button",
        ["S6_2"] = "Release the side/top button – keep holding HOME",
        ["S8_1"] = "Quickly press and release VOLUME UP",
        ["S8_2"] = "Quickly press and release VOLUME DOWN",
        ["S8_3"] = "Hold the SIDE button until the screen goes black",
        ["S8_4"] = "Keep holding side and also hold VOLUME DOWN",
        ["S8_5"] = "Release the side button – keep holding VOLUME DOWN",

        ["Log"] = "LOG",
        ["Copy"] = "Copy",
        ["Clear"] = "Clear",
        ["LogStarting"] = "Starting…",
        ["LogLibsLoaded"] = "libimobiledevice + libirecovery loaded.",
        ["LogLibsFailed"] = "Could not load the libraries: {0}",
        ["BannerLibsFailed"] = "The libraries could not be loaded. See the log.",
        ["LogReady"] = "Ready. Plug in an iPhone.",
        ["LogConnected"] = "Connected: {0} (iOS {1})",
        ["LogToRecovery"] = "{0} → Recovery mode",
        ["LogAuto"] = " (auto)",
        ["LogInMode"] = "{0} is in {1} (ECID 0x{2:X})",
        ["ModeDfuName"] = "DFU mode",
        ["ModeRecName"] = "Recovery mode",
        ["ModeWtfName"] = "WTF mode",
        ["LogRemoved"] = "Disconnected / changed mode: {0}",
        ["LogLeavesRecovery"] = "{0} is leaving Recovery",
        ["LogScanning"] = "Scanning for devices…",
        ["LogNoPhones"] = "No phones found.",
        ["LogLookingRecovery"] = "Looking for a phone in Recovery…",
        ["LogNoRecovery"] = "No phone in Recovery found.",
        ["LogDfuForce"] = "The phone is in DFU – force restart it with the buttons.",
        ["LogRestartingEcid"] = "Rebooting to normal mode (ECID 0x{0:X}).",
        ["LogAutoOn"] = "Auto Recovery: ON",
        ["LogAutoOff"] = "Auto Recovery: OFF",
        ["LogAutoPaused"] = "Auto Recovery is paused while the DFU guide runs.",
        ["LogDfuStarted"] = "DFU guide started.",
        ["LogDfuDetected"] = "DFU mode detected!",
        ["LogDfuCancelled"] = "DFU guide cancelled.",
        ["LogLanguage"] = "Language: English",

        ["ErrNotFound"] = "Phone not found ({0})",
        ["ErrLockdown"] = "Lockdown error ({0})",
        ["ErrEnterRecovery"] = "EnterRecovery failed ({0})",
        ["ErrNoRecovery"] = "Phone not found in Recovery ({0})",
        ["ErrInDfu"] = "The phone is in DFU – force restart it with the buttons.",
        ["ErrReboot"] = "Reboot failed ({0})",
        ["ErrWatchUsb"] = "Cannot watch USB devices ({0}).",
        ["ErrWatchRecovery"] = "Cannot watch Recovery/DFU devices ({0}).",
        ["UnexpectedError"] = "unexpected error",
    };
}
