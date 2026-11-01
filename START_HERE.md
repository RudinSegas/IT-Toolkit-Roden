# Start here — IT Toolkit by Roden Segas v1.0.0

Copyright © 2026 Roden Segas. All rights reserved.

1. Extract **IT-Toolkit-Roden-v1.0.0-portable.zip** to a clean local NTFS folder.
2. Open **IT-Toolkit-Roden** and double-click **IT-Toolkit-Roden.exe** normally. Keep **IT-Toolkit-Roden.exe.config** beside it; the config may be hidden. Do not elevate the whole GUI for normal use.
3. Windows desktop / Framework 4.8 compatibility is required. No toolkit installation, PowerShell command or execution-policy change is needed.
4. Documentation and checksums are in docs. assets contains the original branding; the runtime copies are embedded.
5. For an optional shortcut, open Settings / About → Create my desktop shortcut. Only the current user's desktop is affected, and only on explicit click. Moving the portable folder later requires updating the shortcut.
6. Before operational deployment, follow Windows-Validation.md. This release was cross-compiled and host-tested, not Windows-runtime tested. Do not disable organizational security controls if the unsigned EXE is blocked.

Advanced Diagnostics collects automatically and caches for two minutes; Refresh forces a rescan. Unknown means unverified evidence. Installed Software offers selected-app removal after confirmation, subject to a conservative safety policy; a zero process code alone is not removal success. Cancel an already-running uninstaller in its own window.

Refresh Group Policy deliberately opens a visible current-user Command Prompt for gpupdate /force. Handle Windows prompts yourself; the toolkit reports launch only. Lenovo/Dell actions download verified utilities without executing them, or explicitly open the official page if resolution is unavailable.

Developers: source/build/tests are in the separate source ZIP. The end-user folder contains no source code or developer test executables. See Build.md and Test-Report.md for reproducibility and validation limits.
