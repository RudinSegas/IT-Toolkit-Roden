# Primary references checked during implementation

These references establish platform contracts/package identities. They do not constitute executed Windows acceptance tests or a guarantee of current catalog minor-version availability.

- [WPF overview / .NET Framework implementation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview)
- [WMI ManagementOptions.Timeout limitations](https://learn.microsoft.com/en-us/dotnet/api/system.management.managementoptions.timeout)
- [Named pipe access rights / instance-creation rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
- [CreateNamedPipe: first instance and remote-client rejection](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipew)
- [GetNamedPipeClientProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)
- [GetNamedPipeServerProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid)
- [Supported remote Group Policy update](https://learn.microsoft.com/en-us/powershell/module/grouppolicy/invoke-gpupdate)
- [Remote Group Policy RPC/WMI/firewall prerequisites](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/jj134201(v=ws.11))
- [Windows Settings URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [WinGet install command](https://learn.microsoft.com/en-us/windows/package-manager/winget/install)
- [WinGet source command and structured source export](https://learn.microsoft.com/en-us/windows/package-manager/winget/source)
- [WinGet export: exact installed package identity/version JSON](https://learn.microsoft.com/en-us/windows/package-manager/winget/export)
- [Official App Installer installation channels](https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/troubleshoot-apps-start-failure-use-windows-package-manager)
- [WinGet community repository](https://github.com/microsoft/winget-pkgs)
- [Firefox manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Mozilla/Firefox)
- [Chrome manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/g/Google/Chrome)
- [7-Zip manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/7/7zip/7zip)
- [Acrobat Reader x64 manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/a/Adobe/Acrobat/Reader/64-bit)
- [VLC manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/v/VideoLAN/VLC)
- [Notepad++ package installer-type behavior](https://github.com/microsoft/winget-pkgs/issues/296850)
- [WinGet installer-type configuration](https://github.com/microsoft/winget-cli/blob/master/doc/Settings.md)
- [PowerToys manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/PowerToys)
- [Temurin JRE manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/e/EclipseAdoptium/Temurin)
- [Microsoft OpenJDK manifest family](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/OpenJDK)
- [VS Code Windows installation options](https://code.visualstudio.com/docs/setup/windows)
- [VS Code documented direct download endpoint](https://code.visualstudio.com/docs/supporting/faq)
- [VS Code official download CDN](https://code.visualstudio.com/docs/setup/network)
- [Microsoft WinGet VS Code installer switches issue/reference](https://github.com/microsoft/winget-cli/issues/6248)

The application queries exact IDs against the currently approved source during use. No old minor release, changing URL scraped from a human table, third-party mirror or non-vendor endpoint is pinned as a universal installer. Source verification failures are explicit and do not trigger source-policy resets.

## v3.1 implementation reference review (1 November 2026)

- Microsoft GPUpdate syntax and /wait behavior: https://learn.microsoft.com/windows-server/administration/windows-commands/gpupdate
- Microsoft WUA APIs: https://learn.microsoft.com/windows/win32/wua_sdk/searching--downloading--and-installing-updates (only scan/history APIs used, no download/install engine).
- Firefox full-installer silent /S: https://firefox-source-docs.mozilla.org/browser/installer/windows/installer/FullConfig.html
- PowerToys official releases/silent arguments: https://learn.microsoft.com/windows/powertoys/install
- Microsoft OpenJDK stable major MSI URLs: https://learn.microsoft.com/java/openjdk/download-major-urls
- Temurin Windows MSI installation: https://adoptium.net/installation/windows
- Seven-Zip official download page/repository linkage: https://7-zip.org/download.html and https://github.com/ip7z/7zip
- Adobe deployment/distribution: https://www.adobe.com/devnet-docs/acrobatetk/tools/AdminGuide/index.html
- Ookla intended primary references: https://www.speedtest.net/apps/cli and https://www.speedtest.net/about/terms — retrieval failed in this environment; no enterprise/redistribution permission was inferred. Browser-only fallback chosen. No CLI downloaded, bundled or executed.

Reviewing documentation does not constitute executing a provider/installer on Windows. Endpoint/publisher changes fail closed.

## Prior v3.1.1 native policy references (local API refresh removed in v3.1.2) and feature evidence

- Microsoft RefreshPolicyEx / RP_FORCE: https://learn.microsoft.com/windows/win32/api/userenv/nf-userenv-refreshpolicyex
- Microsoft RegisterGPNotification: https://learn.microsoft.com/windows/win32/api/userenv/nf-userenv-registergpnotification
- Microsoft DISM feature query/enablement: https://learn.microsoft.com/windows-hardware/manufacture/desktop/enable-or-disable-windows-features-using-dism
- Windows file streams: https://learn.microsoft.com/windows/win32/fileio/file-streams
- CreateFileW: https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew

## v3.1.2 official reference points

- Windows MSFT_Disk health/operational enumerations: https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
- Storage reliability counters and DeviceId relationship: https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter
- Lenovo System Update official availability/support notice: https://support.lenovo.com/us/en/downloads/ds012808
- Dell Command | Update official Classic downloads: https://www.dell.com/support/kbdoc/en-us/000177325/dell-command-update

Vendor pages were inspected during development on 1 November 2026. Actual Windows utility downloads/publisher verification were not executed. Direct binary versions/URLs are resolved at runtime, not hardcoded from this inspection.
