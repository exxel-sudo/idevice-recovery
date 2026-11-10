# Third-party notices

The iDevice Recovery source code is licensed under the MIT License (see `LICENSE`).

The released `iDeviceRecovery.exe` also contains the components below. They keep their own licenses. The native libraries are embedded as separate DLLs. On first start they are unpacked to `%LOCALAPPDATA%\iDeviceRecovery\native-1.3.17\` and loaded dynamically from there, so you can replace them with your own builds.

## GNU LGPL 2.1 (or later)

| Component | Source |
|---|---|
| libimobiledevice | https://github.com/libimobiledevice/libimobiledevice |
| libirecovery | https://github.com/libimobiledevice/libirecovery |
| libusbmuxd | https://github.com/libimobiledevice/libusbmuxd |
| libplist | https://github.com/libimobiledevice/libplist |
| imobiledevice-net (bindings and Windows builds, NuGet 1.3.17) | https://github.com/libimobiledevice-win32/imobiledevice-net |

The full LGPL 2.1 text is at https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html.

## Other bundled runtime libraries (from the imobiledevice-net package)

| Component | License |
|---|---|
| OpenSSL 1.1 (`libcrypto`, `libssl`) | OpenSSL / SSLeay License |
| libcurl | curl License (MIT-style) |
| libxml2 | MIT |
| zlib | zlib License |
| bzip2 | bzip2 License (BSD-style) |
| liblzma (XZ Utils) | Public domain |
| libiconv | LGPL 2.1 |
| libzip | BSD 3-Clause |
| PCRE | BSD |
| readline | GPL 3.0 (*command-line tools only; not used by this app*) |
| libusb | LGPL 2.1 |
| pthreads-win32 | Apache 2.0 |
| getopt | BSD |
| Microsoft Visual C++ runtime (`vcruntime140.dll`) | Microsoft redistributable |

## .NET

The exe is a self-contained .NET 8 / WPF build. .NET is MIT-licensed: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

---

Not affiliated with Apple Inc. iPhone is a trademark of Apple Inc.
