# iDevice Recovery

A portable Windows tool that can **put an iPhone into Recovery mode automatically when it is plugged in**, lets you **reboot a phone that is stuck in Recovery** with one click, and walks you through the **DFU button sequence** with a live countdown and automatic DFU detection.

Single `.exe`, no installer, no .NET runtime needed. UI in English and Swedish (switch in the title bar).

> 🇸🇪 *Svenska:* Portabelt Windows-verktyg som sätter iPhone i Recovery automatiskt vid inkoppling, tar ut telefoner ur Recovery med ett klick och guidar till DFU med nedräkning. Byt språk uppe till höger.

## Features

- **Auto Recovery on connect.** Off at startup; turn it on with the switch. While it is on, any iPhone connected in normal mode is sent to Recovery. There is also a **To Recovery** button per phone.
  - Older iOS accepts the request without pairing.
  - **iOS 16 and later** answers `SessionInactive` unless the computer is trusted. The app then triggers the *Trust This Computer* prompt. Unlock the phone, tap **Trust** and enter the passcode, and the app retries for up to 2 minutes. Pairing is needed only once per computer. Other tools that use libimobiledevice, such as blobsaver, have the same requirement.
- **Only once per phone** (on by default), so a phone does not end up in a Recovery loop.
- **Reboot to normal.** Kicks a phone out of Recovery (`auto-boot=true` + reboot). Phones you rebooted this way are never sent back automatically.
- **DFU guide** for:
  - iPhone 8 and later
  - iPhone 7
  - Home-button models

  It shows a step-by-step countdown and detects whether the phone landed in **DFU**, in Recovery, or in neither. The model is pre-selected when a phone is connected.
- **Device list** showing mode (Normal / Recovery / DFU), model, iOS version, UDID, ECID and chip. Right-click to copy the UDID or ECID.
- **Driver status indicator.** One click opens the Microsoft Store page for *Apple Devices*.
- **English / Swedish UI** (SV | EN switch in the title bar; the choice is remembered).

### Why can't DFU be automatic?
Apple provides no USB command that enters DFU. DFU is handled by the boot ROM and can only be reached with the hardware button sequence. The tool times the sequence for you and confirms the result.

## Requirements

- Windows 10/11 x64
- Apple's USB driver, from either:
  - **Apple Devices** from the Microsoft Store (`winget install 9NP83LWLPZ9K`)
  - **iTunes**

  Without it Windows cannot talk to the phone.

  The Store version of Apple Devices does not install a Windows service. Its USB component (`AppleMobileDeviceProcess`) normally starts only at sign-in, so it is not running right after installation. iDevice Recovery detects this and starts it automatically. You can also click the driver indicator to start it.

## Usage

Download `iDeviceRecovery.exe` from [Releases](../../releases) and run it. On first start the native libraries unpack to `%LOCALAPPDATA%\iDeviceRecovery\`.

The exe is not code-signed, so Windows SmartScreen may show *"Windows protected your PC"*. Click **More info → Run anyway**.

## Building

Requires the .NET 8 SDK.

```powershell
cd src
dotnet publish -c Release -o ..\dist
```

The output is `dist\iDeviceRecovery.exe`, a self-contained single-file build of about 67 MB.

## How it works

- Normal-mode devices are found with **libimobiledevice** (via Apple Mobile Device Service / usbmuxd). The app sends the lockdown `EnterRecovery` request.
- Recovery and DFU devices are found with **libirecovery**. Leaving Recovery means setting `auto-boot=true`, then `saveenv` and `reboot`.
- The native Windows x64 builds come from the [imobiledevice-net](https://github.com/libimobiledevice-win32/imobiledevice-net) NuGet package (1.3.17). They are embedded in the exe and unpacked at startup.
- UI: WPF on .NET 8.

## License

The source code is released under the **MIT License** (see [LICENSE](LICENSE)). You may use, modify and distribute it, including commercially, as long as the copyright notice is kept.

The released exe also bundles third-party libraries under their own licenses. These include libimobiledevice, libirecovery, libusbmuxd and libplist under the **GNU LGPL 2.1**. They are loaded dynamically and can be replaced. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with Apple. iPhone is a trademark of Apple Inc.
