# Selected-application removal

Use Advanced Diagnostics → Installed Software, select an eligible app and click Uninstall selected. Confirm its name, publisher and version. The registered interactive vendor UI or Windows Installer opens. No silent flags are invented; the toolkit does not authorize automatic restart/logoff. Fresh complete registry inventory is required before Successfully Removed can be shown.

The conservative safety policy refuses protected system/runtime/security apps, shell/script/proxy commands, unsigned EXEs, ambiguous MSI commands, remote/relative paths and machine EXEs outside administrator-protected Program Files. Use Windows Installed apps/vendor management for unsupported entries. This interface does not enumerate Win32_Product or remove Store/MSIX/portable software.

An active operation holds a per-user cross-instance lock. If an EXE forks and the launcher exits before the app registration disappears, the result is Verification Inconclusive. A persistent per-application pending marker prevents a second launch when the previous completion cannot be established. Cancellation after launch is advisory; use Cancel in the vendor window. A known native cancel/failure releases the pending marker; missing/inconclusive completion retains it.

To recover a retained marker: first independently verify that the previous vendor uninstaller and its child processes have ended and no installation/removal is running (restarting Windows is a conservative way to end interactive processes, but only when you choose to do so). Refresh Windows Installed apps. If the application still requires another attempt, manually delete only the specific .pending path shown in the toolkit result under your own LocalAppData/IT-Admin-Toolkit/UninstallLocks. Never clear it while a vendor operation is active. The toolkit does not automatically remove uncertain-operation guards or restart Windows. Do not delete other logs/locks or application files as a substitute for removal.

Already Absent means fresh complete inventory contains no matching selected registration/application; it does not certify erasure of every vendor file or user setting. MSI or vendor exit zero alone is insufficient. Restart Required and Verification Inconclusive are distinct results.

Actual Windows removal, UAC, vendor cancellation/forking and pending-marker recovery remain acceptance tests; the build host cannot run them.
