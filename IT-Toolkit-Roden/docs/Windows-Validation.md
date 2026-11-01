# Windows 11 acceptance — v1.0.0

**NOT executed on the Linux development host.** Use an authorized test PC/VM for state-changing actions. Record Windows build, locale, DPI, account type, EXE hash, pre-state, observed output, independent post-state and redacted logs. Do not disable security controls.

## Start and read-only automated fixtures

1. Extract the release into a clean local NTFS directory. Keep `IT-Toolkit-Roden\IT-Toolkit-Roden.exe.config` beside the EXE. Double-click `IT-Toolkit-Roden\IT-Toolkit-Roden.exe` normally. Expect the dark GUI with no console, startup PowerShell or UAC. Verify About shows 3.1.2, Roden Segas and the exact copyright.
2. From a Command Prompt in the extracted developer/source archive's root folder run:

```bat
validation\Toolkit.Tests.exe --process --windows
```

This test executable intentionally uses a console and opens a WPF window. It materializes pages/categories with binding-error tracing, verifies read-only Progress notifications reach the real ProgressBar, editable bindings, navigation scroll reset, output height/collapse, metadata/report serialization, native NTFS image/config/pipe protections, harmless MOTW Unicode-path handling and unsigned-file rejection. It also pumps the WPF dispatcher with injected diagnostic results to verify automatic entry/loading, category switching, late-result rejection and cache reuse. It does not install software or repair Windows. NTFS temp storage is required for the file fixtures.

3. Run `validation\Toolkit.Tests.exe --probe` separately for the explicit read-only elevated-helper probe. Check UAC accept/cancel and standard-user alternate-admin credentials. Verify the whole GUI stays unelevated. Repeat with missing/tampered config, multiple instances and security-policy denial.
4. Test 1024×768, 1280×720 and 1920×1080 at 100/125/150/200% scaling. Visit all pages, resize, scroll, expand details, collapse output. No binding exceptions, clipped operation controls or unexpected page resets should occur. Compare the retained Dashboard/System/Network/Software/Reports screens to v3.1.1.

## Advanced Diagnostics

- Enter each real category without Refresh. Observe loading then timestamp/summary/results. Change categories during collection; an old result must never replace the new selection. Revisit within two minutes (cache), after two minutes (recollect), and press Refresh (force rescan).
- Overview and BIOS: compare identity, BIOS version/date, manufacturer/model and memory to Windows System Information. Driver details should be available under Technical evidence without a giant primary table.
- Storage: compare disk number/model/capacity and provider states to Disk Management/Get-Disk; inspect SSD/HDD/NVMe detection, multiple operational statuses, temperature/counters, volume free/used/%/filesystem and BitLocker. Test unavailable storage/SMART/BitLocker providers and low-space fixtures. Unknown data must never become Healthy. Healthy is explicitly scoped to the Windows provider, not a full SMART test.
- Devices: compare error count/codes/explanations to Device Manager. Toggle problem-only, search live, select details. Test no problems and unavailable/partial inventory. No errors is not asserted when all inventory is missing.
- Domain: standalone and authorized AD-joined machines; compare domain/DC/DNS, secure-channel evidence and GPResult. TCP389 reachability alone must not become authentication/trust success. Denied/localized/inconclusive native evidence remains Unknown.
- Network: active adapters, addresses, DHCP, gateway/DNS and link-local warnings. Existing targeted tests, snapshot save/load/compare, explicit DHCP operations and browser Speedtest remain accessible.
- Update diagnostics: services/history/latest successful installation/error count/reboot evidence, no automatic availability scan or installation. Pending Reboot gives Restart Required / No Restart Detected / Unknown with its indicators. Performance gives bounded sample/units, not continuous monitoring.
- Export selected category/report; summaries/primary records remain readable and default redaction remains enabled. Raw technical evidence is not automatically included in privacy reports.

## Services & Events

- Service table automatically loads; test display/name search, status filters, selected description/state/startup and disabled controls. No service inventory dump should appear in Activity Output.
- On a disposable service you are authorized to control, test Start when stopped, Stop when running and confirmed Restart. Independently inspect the service after each action. Confirm the result is beside the selected service. Change state outside the toolkit before clicking: fresh pre-check/helper check must avoid redundant action. Disabled/non-stoppable/transitional/protected services must be explained, not blindly altered. Test UAC cancellation, insufficient service rights, timeout and service removal during operation.
- No event query occurs merely on entry. Choose System, Application, Security and permitted operational logs explicitly. Test Critical/Error/Warning/Information/All, days 1–30, exact source and ID filters. Test malformed values, denied Security, cleared logs and unavailable channels.
- Use a large existing log: maximum 500 rows per page, Older events uses a record-ID cursor, no whole-log load. Select a long event and compare its complete message/XML to Event Viewer; table preview may truncate but full detail must not. Select another event while details load; late details must not replace the selection. Copy full detail.

## Local and remote Group Policy

- Click **Refresh Group Policy** normally. Expect visible Command Prompt running fixed `gpupdate /force`, same initiating identity, no UAC/helper, no redirected streams. Windows prompts remain interactive; answer manually. Window stays open on completion. Toolkit reports only launch, never policy success. Test standalone/domain machines and localized prompts. No automatic reboot/logoff.
- Starting the entire toolkit elevated deliberately changes current-process identity; normal acceptance uses unelevated startup.
- GPResult user/computer and remote GPUpdate retain prior behavior. Test authorized RSAT/domain management, missing RSAT, invalid targets, offline targets, denied remote scheduling/event access. Scheduling is not reported as fully applied. No automatic firewall/WinRM/authentication change.

## Vendor utility downloads

- Lenovo System Update and Dell Command | Update Classic: inspect official metadata, exact product/architecture compatibility, allowed redirects, download progress, native Downloads folder (including redirected/Unicode paths), Zone.Identifier and trusted publisher. Compare manual signature Properties with toolkit result. Expect Explorer to select the verified file; no installer process should start.
- A gated/changed/ambiguous/unavailable vendor page must produce a clear **No installer downloaded** message and open the official page. Do not count that as download success. Never substitute Dell Universal/ARM/SupportAssist or another Lenovo utility.
- Test offline, 403/404/503, redirects, invalid TLS/signature/publisher, metadata timeout (45 seconds), stalled streaming deadline (15 minutes) and cancellation. Partial downloads should be removed, with explicit cleanup failure if blocked. No security failure may launch an installer.
- Lenovo maintenance/EOL notice must be displayed; after published EOL, automatic direct download is refused in favor of current official information. Model-specific compatibility is reviewed manually before any user-initiated installation.

## Retained state-changing acceptance

- NetFx3: already enabled with clear reboot indicators skips enable/UAC; disabled invokes authorized DISM /NoRestart then fresh state query; pending yields Restart Required; unknown denies blind enable. Respect WSUS/source restrictions.
- SFC/DISM: genuine streaming output, responsive UI, independent repair evidence where available; no fabricated success from exit zero and no automatic restart. Active servicing is not force-killed on Cancel.
- Every software catalog profile: installed/current/older/unknown-latest, scopes, official version/hash/signature identity, blocked download, post-install registry evidence, restart code, failure, cancellation and cleanup. Unknown latest is not Up to Date. Test Firefox Unicode/MOTW and VLC transient HTTP failures specifically. Existing HTTP fixtures do not certify vendor installations.
- DHCP selected-adapter renew/release: confirmation, DHCP precheck, fresh post-state; no static configuration change. Unchanged renewal without lease-time evidence stays inconclusive. DNS flush retains evidence rather than asserting comprehensive network health.
- Windows Update Settings handoff must not report an installation result. Test report redaction, startup/logging failures and multi-instance servicing coordination.

Record every Windows case as PASS/FAIL/NOT RUN with actual evidence. The supplied host results must not be relabeled as Windows execution results.

## Public v1.0.0 release acceptance

- Extract the portable ZIP with Windows Explorer. Root should contain only IT-Toolkit-Roden.exe, its adjacent config, docs and assets. Check whether config is Hidden; if the extractor ignores the attribute, leave it visible or set Hidden in Properties. Never move it. Confirm startup loads CLR config, title/About/reports show 1.0.0 and the exact Roden Segas product/copyright.
- Check the native EXE icon in Explorer at small/large sizes, Alt-Tab/taskbar/window icon, Dashboard and About logo. Loose assets may be renamed temporarily to confirm startup still uses embedded resources. Restore them for distribution.
- Run the explicit read-only helper probe after the executable rename, including UAC cancel/alternate administrator and spaces/Unicode in the portable directory. Existing image/config/pipe security must remain intact. Test a missing/modified config is refused by the lease; do not alter system trust settings.
- Click Create my desktop shortcut only on your own test account. Verify its name, target, working directory and embedded icon; relaunch from it. No shortcut should be created merely by startup or extraction. Remove the test shortcut afterward.
- Installed Software: select a disposable, authorized test app; confirm name/publisher/version. Test already absent, changed identity/version, no command, protected component, valid MSI product code, quoted EXE, unsafe shell/path, unsigned EXE, denied machine-path ACL, UAC cancel, vendor cancel, failed exit, reboot-required code and a forking uninstaller. No test should remove a real system/security component. No uninstall command should enter the privileged helper allowlist.
- Verify fresh inventory independently in Windows Installed apps/registry after removal. Exit zero while the app remains registered must be inconclusive; incomplete inventory cannot prove absence. Start a second toolkit instance while an uninstaller is active and confirm duplicate removal is refused. Cancel does not kill an active uninstaller or automatically reboot/log off.
- Notepad++: test both clean install and explicit update from an older version; already installed/current skips unnecessary work. Confirm runtime resolution selects the official stable x64 MSI with SHA-256 from official release metadata, WinVerifyTrust accepts the commercial NOTEPAD++ publisher, MOTW remains, and msiexec uses /i, /qn, /norestart, REBOOT=ReallySuppress. Compare installed registry version to the resolved release. Test missing/mismatched digest, invalid signature, offline/revocation/proxy restriction, native failure/restart codes and safe cleanup. Do not import the legacy self-signed root.
- Capture actual Dashboard, Installed Software and About screenshots at 100% scaling only after successful startup. Use a test VM, redact hostname/user/IP/domain/event data and state Windows build/DPI with the image. These screenshots may then replace the README's explicit not-captured disclosure; do not label generated artwork as runtime screenshots.
