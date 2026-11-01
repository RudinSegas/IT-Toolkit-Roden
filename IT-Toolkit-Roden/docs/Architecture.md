# Implemented architecture

Production source is compiled into one native managed Windows GUI image plus its runtime configuration. WPF XML is embedded and loaded by C# from the assembly, never extracted or loaded from a writable resource directory. The normal Windows build uses standard .NET Framework MSBuild/C# targets with embedded XAML; cross compilation uses the same C# sources/resources. No old PS1/PSM1 application code is included in the new source or portable runtime.

| Folder | Responsibility |
|---|---|
| src/Toolkit.App | STA entry/error handling, embedded main window/theme, MVVM properties and commands, UI batching/navigation |
| src/Toolkit.Core | Inventory DTOs/provider slots, typed output/result contracts, fixed command allowlist, quoting/host validation, byte-stream process engine/parser, bounded structured logger and protocol |
| src/Toolkit.Windows | Local WMI/registry/native inventory, update/reboot/storage/devices/performance/domain providers and network/services/events diagnostics |
| src/Toolkit.Privilege | Native image/namespace/config guards, process/token identity, local restricted pipe and separate elevated process role |
| src/Toolkit.Software | Immutable catalog, WinGet registration/source/identity checks, sequential install/update/verification, Authenticode and per-app download policy |
| src/Toolkit.Reporting | Escaped offline HTML and JSON, selected sections, privacy redaction |
| tests | Executable test suite/controlled C# child, Windows-only WPF/NTFS/pipe fixture and release inspections |
| build | Optional cross compiler/test host helpers; no application runtime dependency |

Commands never run long work on the WPF dispatcher. Inventory provider calls run on background tasks, with one outstanding call per provider slot and a UI-visible deadline. Diagnostic/process work runs through a single active operation in each GUI. Multiple GUI instances are supported; native privileged servicing is coordinated by an admin-only file lease.

The same EXE's worker mode is a separate process and code path. It does not initialize WPF pages and never accepts arbitrary command text. Only fixed local administrator operations cross that boundary. The output protocol is reusable for future tested fixed operations, without adding an unrestricted console endpoint. Software uses catalog-only direct profiles resolved inside the helper for machine installs; VS Code stays in the current user context. WinGet is an explicitly selected alternative. No user-selected downloaded code or executable path is accepted.

Public and optional enterprise behavior share one codebase. The remote Group Policy action requires explicit session enablement, uses the current Windows identity and checks supported prerequisites during execution. No internal infrastructure settings or NinjaOne integration is present. PC Setup/advanced Office/interactive console are excluded, not represented by clickable placeholders.

## Reference/test migration record

Original reference: the newly attached v2.0.0 archive, SHA-256 `a297d5c4306606f6bed5044c441c09814c894231201b8564ac56dfe7b08b8dbd`. Its 62 manifest entries were verified before migration; the archive was not modified.

| v2 reference | v3 treatment |
|---|---|
| MainWindow/Styles, dashboard/system pages | Theme/layout and field/cache/copy behavior ported; imperative UI rewritten to binding/templates |
| SystemInfo/Network | Data collection/validation/limitations ported to native managed/native Windows APIs |
| Maintenance/Terminal/Execution | Fixed operation semantics preserved; new byte-stream reader/parser/threading implementation and tests |
| Elevation/Bootstrap/JobJournal/Protocol | Replaced by compiled worker/actual peer checks/pipe; legacy writable journal not used |
| Presentation/Reporting/Logging | Native typed summaries, HTML encoding/privacy defaults and richer bounded lifecycle/output logging |
| Live-output/process timeout fixtures | Replaced with C# controlled child/decoder/deadline fixtures; original PS runtime not required |
| UI/worker acceptance | New compiled Windows UI/locking/pipe fixture and explicit actual UAC acceptance procedure |
| Software | New catalog/providers and verification semantics; no forced old Java default or organization sources |
| WUA/Office/PCSetup/Console assertions | Deliberately retired because their features were removed from the approved v3 scope |

Original scripts/tests remain in the user-supplied v2 archive for historical reference. They are not copied into the public v3 package, which would retain removed application code and organizational history. New tests cover applicable contracts and clearly distinguish development-host fixtures from unexecuted Windows acceptance.

## v1.0.0 corrective architecture

StateWorkflow owns typed desired-state decisions and postcheck verdicts. ProbeMessage carries the verdict, human evidence summary and nullable native installer code independently of helper completion. DownloadTransport owns bounded HTTPS retries; InternetZone owns native ADS writing. MaintenanceWorkflow owns feature-state gating and action-specific verification. PolicyRefresh retains bounded read-only local/domain and remote event evidence; local refresh itself is the separate interactive UserActions launcher. Advanced Diagnostics exposes contextual VM flags and cached category snapshots; only navigation resets page scroll.

## v1.0.0 presentation and workflows

AutoDiagnostics adds cancellable category orchestration, request-generation guards and a two-minute session cache. HelpdeskDiagnostics separates concise Records/Summary from optional TechnicalRecords. Exported reports retain summaries and selected primary records; raw technical evidence is intentionally not added to privacy reports automatically. No database or background agent is required.

ServiceEventBrowser uses bounded native WMI/EventLog queries. ServicesEventsViewModel owns local observable tables, selected details and result text; inventories do not populate global operation output. Service mutation remains a typed helper operation, with fresh checks on both sides. Local policy refresh is now a separate fixed interactive UserActions launcher; previous RefreshPolicyEx logic was removed. Remote evidence is retained.

VendorUtilityDownload resolves official metadata then streams into a unique protected user Downloads directory. It verifies MOTW/publisher before returning a file for Explorer selection. It has no installation entry point. Failed resolution returns an explicit official-page fallback result. Existing software installation providers remain separate.

## Public v1.0.0 incremental additions

The actual running image path remains the source of helper launch/peer identity checks; no executable basename is hardcoded into elevation. The adjacent config is still pinned against Toolkit.App.config. Internal ITAdminToolkit pipe / IT-Admin-Toolkit log and servicing namespace names intentionally remain stable to preserve cross-version coordination and existing user preferences. Public product and output names change independently of these private namespaces.

UninstallService is not a privileged helper endpoint. It rereads the selected registry hive/view/key, constructs a constrained MSI product-code or absolute EXE plan, validates trust/eligible locations and invokes Windows ShellExecute. Machine scope requests runas only when the current token is not elevated. Registry scope, product identity and protection flags are rechecked before launch; fresh complete inventory is required for Successfully Removed. No raw command/path is sent over the helper pipe. MSI arguments suppress automatic restart; EXE arguments remain registered interactive arguments.

Logo/Icon are assembly resources used by WPF DynamicResource image sources. The same ICO supplies Win32 executable resources. Loose assets are for documentation/reuse, not startup loading. Desktop shortcut creation uses Windows COM only after an explicit About action.
