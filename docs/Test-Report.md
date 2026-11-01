# v1.0.0 release validation report — 1 November 2026

**IT Toolkit by Roden Segas**  
**Copyright © 2026 Roden Segas. All rights reserved.**

## Scope and environment

Starting point: attached IT-Admin-Toolkit-v3.1.2-complete(2).zip. The existing C#5/WPF/MVVM/Framework 4.8 application and regression suite were retained. Changes are limited to public identity/packaging/branding, selected-app uninstall, the Notepad++ profile and supporting tests/documentation.

Build host: Linux x64, Roslyn hosted by development-only PowerShell 7.4.6, official Framework 4.8/WPF reference assemblies. No Windows desktop runtime is available. These build tools are not application runtime dependencies.

**Status: compiled and host-tested; NOT fully Windows-validated.** The binary is unsigned.

## Automated checks executed here

- Production and test compilation: C#5, x64, .NET Framework 4.8 references. The final output is IT-Toolkit-Roden.exe with the exact adjacent IT-Toolkit-Roden.exe.config.
- **267 core assertions passed**, including retained state-aware/protocol/binding/output/logging/HTTP/inventory/report/cache/service/event tests and focused public-release tests.
- New release assertions cover 1.0.0 assembly/product constants, embedded logo/icon presence, protected uninstall records, hive/view/key identity, MSI target GUID/arguments, EXE command/path/proxy rejection, original argument preservation, scope-based elevation, exclusive lock behavior, independent absence/partial-inventory/cancel/failure/restart verdicts, no generic uninstall helper endpoint, Notepad++ MSI/hash/version/origin/profile restrictions and explicit WinGet MSI family selection.
- Actual harmless Linux child processes exercise production ProcessEngine: partial stdout/CR, stderr, output before exit, exit 7, read-only timeout/cleanup, cancellation/PID exit and duplicate-process refusal. These are not Windows servicing/installer/uninstaller tests.
- Controlled production HTTP retry fixtures cover transient errors, Retry-After, exhaustion, cancellation and origin-limited redirects. Vendor resolver fixtures are not live downloads or installations.
- Static XML/source audit: **82 command bindings**, explicit read-only Progress direction, fixed interactive policy launch, native MOTW path and public version; dynamic actual-image helper launch and adjacent-config lease. Preserved System/Network/Software/Reports templates match their prior fingerprints. Dashboard matches after excluding only its requested branding header.
- Resource inspection: production assembly name IT-Toolkit-Roden; assembly/file public version 1.0.0.0, displayed/informational version 1.0.0, exact product. Embedded MainWindow.xaml, Theme.xaml, App.config, Logo and Icon match source bytes; adjacent config equals embedded config.
- PE inspection: Windows GUI x64, CLR v4/Framework 4.8 metadata, asInvoker/uiAccess=false, public manifest identity/version, native icon/group-icon resources with **16/24/32/48/64/128/256** pixel images. This is resource inspection, not Windows rendering.
- Packaging validation: both ZIP CRCs, every release SHA-256 manifest entry, clean portable root/required config, Hidden config attribute, developer-file exclusion, source completeness and documentation asset links are checked after packaging. Final EXE checksum is included in portable docs and developer validation.

Raw outputs are included in the developer ZIP's validation directory. No unexecuted Windows assertion is counted among the 267 core assertions.

## Compiled Windows fixtures — NOT executed

Run from the extracted source release:

```bat
validation\Toolkit.Tests.exe --process --windows
```

Retained fixtures instantiate real WPF pages/categories, collect binding-error traces, check INPC Progress reaches the actual ProgressBar, editable selections, layout/scroll/output behavior, injected automatic category switching/cache/stale-result guards, native MOTW Unicode paths, unsigned-file rejection, image/config/namespace leases and pipe rights/identity. The lease fixture uses a renamed executable path and its adjacent config. JSON metadata tests additionally exercise the actual Notepad++ release parser selecting MSI over EXE, and rejection of missing SHA-256. Framework JSON/report serialization checks also remain Windows-only.

`--probe` separately requests explicit UAC for a read-only helper test. No automatic fixture installs software, uninstalls applications or repairs Windows.

## Remaining Windows acceptance

- Actual GUI startup, DPI/layout, icon/taskbar appearance, hidden-config extraction behavior, optional COM shortcut creation and Windows MSBuild.
- Real renamed-image helper/UAC/alternate-admin/NTFS/pipe behavior; policy-controlled application acceptance.
- Actual Notepad++ download, commercial publisher trust/revocation, MSI installation/update, native codes, post-version evidence and cleanup. The old EXE dual-signature risk was identified in source; the user's exact failed Windows installation was not reproduced without its log/runtime.
- Actual MSI/EXE uninstall, protected registration handling, UAC/vendor cancellation, forked vendor processes, independent post-inventory, duplicate/pending-marker guard/recovery, restart-required results and no unsolicited reboot/logoff.
- Real software catalog/providers, SFC/DISM/NetFx3, services/events/domain/network/WUA/BitLocker/storage and all previously unverified Windows operational cases.
- Real Windows screenshots were not captured. README artwork is branding, not a GUI screenshot or runtime acceptance evidence.

Follow Windows-Validation.md and record real PASS/FAIL/NOT RUN results before claiming Windows acceptance. Missing data and zero process codes never establish system health or successful removal.
