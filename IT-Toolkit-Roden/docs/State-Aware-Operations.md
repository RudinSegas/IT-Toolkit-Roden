# State-aware operations — v1.0.0

All configuration actions report evidence separately from process/API results. Shared `StateWorkflow` distinguishes AlreadyConfigured, SuccessfullyChanged, Failed, RestartRequired and VerificationInconclusive. Unknown data never means healthy. Explicit repair/force/restart is an intentional operation and is not skipped just because a superficial check passes.

| Operation | Check and decision | Execute | Independent post-check |
|---|---|---|---|
| Direct software install | Complete uninstall inventory; installed package skips reinstall, update status unknown | Fixed vendor profile after HTTPS, digest/publisher/MOTW checks | Fresh inventory, publisher/name/scope and version; VS Code executable signature additionally |
| Direct software update | Resolve official release; exact normalized version equality skips download/UAC | Explicit update permitted when latest version unknown; clearly labeled | Changed version and known-release match required; unchanged/ambiguous evidence inconclusive |
| WinGet | Exact export inventory required; known independent official version can skip; otherwise WinGet resolves applicability | Fixed exact package/source/noninteractive arguments; no forced reinstall option | Fresh exact export identity/version plus requested scope evidence |
| NetFx3 | Standard-user WMI feature evidence when reboot indicators are clear; known Enabled skips UAC. Otherwise helper runs DISM /English /Get-FeatureInfo; pending skips, unknown blocks mutation | Disabled feature only, /NoRestart | New DISM feature-state query, native code and pending state |
| Start/Stop service | Fresh state, known desired state skips; GUI pre-check avoids UAC; helper rechecks | Validated native service name; protected security/core services excluded; disabled/transitional/non-stoppable states block inappropriate actions | Fresh WMI instance after observed requested state |
| Restart service | Confirm explicitly; inspect current state | Stop if running, wait stopped, start and wait running | Fresh WMI instance; no claim based on method code alone |
| DHCP release/renew | Selected GUID must still identify a DHCP adapter. Known absent usable IPv4 lease skips release. Renew is explicit repair | Native WMI method only on selected adapter | Fresh adapter object; usable IPv4 observation. Unchanged renewal address without independent lease-time proof is inconclusive |
| DNS flush | Read DNS cache before; explicit repair may run despite apparently normal cache | Fixed ipconfig /flushdns | Read cache again; concurrent population prevents a strong clean-state assertion, so successful command remains inconclusive |
| SFC / DISM | Read pending servicing/reboot evidence; explicit diagnostic/repair remains allowed | Genuine native output, no fabricated percent or forced servicing cancellation | RestoreHealth followed by CheckHealth; other scans retain original evidence. Repair verification remains inconclusive unless independently established |
| Local policy force refresh | Explicit interactive request | Fixed visible cmd /k gpupdate /force in current context; no helper or stream redirection | Intentional exception: only window launch reported; Windows presents output/prompts and the technician verifies the result |
| Remote policy force refresh | Validated host/name resolution; attempt authorized remote GroupPolicy event read | Fixed RSAT Invoke-GPUpdate, current Windows credentials | Independent remote event read after scheduling; submission never reported as fully applied |
| Windows Update | Read-only diagnostics/history/service/reboot evidence; availability scan explicitly requested | Toolkit never installs updates. Settings handoff leaves mutation decisions to Windows | Scan returns configured WUA provider evidence; opening Settings never reports installation success |

Diagnostics, GPResult, exports, browser/Settings handoffs and inventory refreshes do not configure the machine. Repeating a requested read-only refresh is intentional and does not require a mutation skip rule. There is no generic arbitrary configuration or service endpoint.

Evidence limitations are visible outcomes, not hidden successes. The local policy launcher leaves all prompts to Windows and makes no policy-completion claim. SFC, DNS flush and unchanged DHCP renewal conservatively remain inconclusive. No unknown feature state causes blind enablement. No cross-process helper status is described as an installer exit code.

| Selected-app uninstall | Reread exact hive/view/key and confirmed identity; absent skips, changed/protected/invalid blocks | Registered interactive EXE or product-code MSI; user confirmation; scope-based UAC | Fresh complete inventory; Successfully Removed only with absence, otherwise Failed / Restart Required / Verification Inconclusive. Cancel is explicit and active uninstaller is not killed. |

Already Absent means the selected registration no longer exists before launch; it is not a statement about every file on disk. Notepad++ requires stable official x64 MSI metadata, SHA-256 and trusted NOTEPAD++ publisher; existing state/version skip and post-install evidence remain shared with other installers.
