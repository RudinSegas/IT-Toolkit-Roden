# Troubleshooting

## Nothing starts / Windows blocks the EXE

Verify this is Windows x64 with .NET Framework 4.8/compatible later Framework and an interactive desktop. Keep the EXE/config together. This unsigned release can be blocked by SmartScreen, WDAC, AppLocker, Defender or enterprise tooling. Preserve the actual Windows message, verify the supplied SHA-256 and use your approved trust/distribution process. Do not change Execution Policy or disable protection to start the GUI. If the OS/CLR blocks before Main, no managed application dialog/log can be created.

## Managed startup error

The native dialog includes the actual error and diagnostic log path if logging initialized. Reports also shows the session log. Normal location: `%LOCALAPPDATA%\IT-Admin-Toolkit\Logs\Toolkit-*.jsonl`. If profile access is denied, logging unavailability is visible; no hidden elevated fallback is used. Send the exact error, Windows version/language and redacted log, not credentials.

## Unavailable system information

Some WMI/registry providers are slow, missing or restricted. Other sections remain usable. Ten-second UI deadlines do not abort WMI threads; a still-running provider is not replaced repeatedly. Refresh after it finishes. The five-minute cache is a snapshot. Compare memory/disk/route values at the same time and interface/VPN context. Activation output is evidence, not a product-key or license audit.

## Helper does not connect / UAC cancelled

Start with Settings / About's read-only helper probe, not a repair. Cancellation is a normal result. Use a clean trusted local NTFS folder; retain the exact .exe.config, remove unexpected loose DLLs by restoring a clean distribution, and avoid shares/reparse/cloud placeholders for elevation. File locks/reparse checks fail closed. If Windows blocks the helper, retain the error/exit and applicable Windows/security logs. An alternate administrator's worker log is under that administrator's profile; ask that administrator to retrieve it through an approved process.

## No live output or incorrect characters

Check that the operation is still running, scroll/auto-scroll state, command exit code, log path and output truncation notices. SFC's redirected profile is UTF-16LE; DISM/local policy use OEM and WinGet UTF-8. Actual localized Windows behavior remains unverified in this delivery. Report exact build/language/tool, whether output appeared before exit, and redacted worker/session logs. Percentages are never invented. The retained view and exported transcript have separate bounded limits.

## Servicing lock / second instance

Wait for the active servicing operation to finish. The ProgramData coordination file is data-only and intentionally persists; its mere presence does not mean a job is running. The file handle, not a stale filename, provides exclusion. Do not delete another active process's files or kill Windows servicing for cleanup. An unexpected owner/reparse path is a security failure, not a reason to weaken ACLs.

## WinGet missing / blocked / source rejected

Use Software → Install / repair WinGet to open Microsoft's App Installer channel. Complete the supported Store operation, return and Detect WinGet. Windows account registration differs; the current GUI account is the one checked. If Store or sources are restricted, respect that result and consult the administrator or use the app's official vendor page. This toolkit neither sideloads unapproved packages nor resets enterprise policy/sources. An old client missing a required option must be updated through the approved mechanism.

## Installer ended but result is inconclusive

This is intentional when exact before/after structured identity/version evidence cannot be established, the requested scope cannot be verified, an update did not change version or reboot is pending. Review original WinGet exit/output, independently inspect Apps & Features/file version, then refresh status. Registry display names alone are not enough to claim successful installation. Available latest versions are in original Package details; the application does not guess a version from localized tables.

## Direct vendor download fails

The official direct catalog supports fixed vendor profiles; Adobe may withhold a resolvable distribution link and 7-Zip requires a published digest if unsigned. VS Code is current-user; other direct profiles are machine-wide. HTTPS/redirect/signature/publisher/hash/zone/size restrictions fail closed. A proxy/OCSP/security restriction or changed vendor CDN can legitimately block it. No file executes before validation; use the official vendor page rather than weakening validation. Installer-held files may remain until the installer finishes; cleanup does not force-stop the process or recursively remove arbitrary folders.

## Remote GPUpdate unavailable

Explicit enterprise session enablement, GroupPolicy RSAT at the system module path, authorized current identity and preconfigured RPC/WMI/Task Scheduler access are required. Normal local administrator rights do not imply remote domain authorization. Use a confirmed hostname/FQDN when IP reverse/forward resolution fails. The optional adapter uses Windows PowerShell 5.1; missing/restricted modules are errors, never policy-bypass triggers. A submitted request must be verified separately in target GroupPolicy Operational logs.

## Windows Update action ignored

Use Open Windows Update, then the native UI. Settings URI availability varies. No status in the toolkit claims that opening a page completed an update. Windows and organization policy remain in control.
