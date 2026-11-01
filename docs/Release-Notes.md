# IT Toolkit by Roden Segas — v1.0.0 Initial Public Release

Copyright © 2026 Roden Segas. All rights reserved.

This public release is a targeted continuation of internal v3.1.2: same native WPF/MVVM application, Framework 4.8 compatibility and portable/security architecture. Public version numbering restarts at 1.0.0.

Deliverables:

- IT-Toolkit-Roden-v1.0.0-portable.zip: executable/config, end-user documentation and original branding assets.
- IT-Toolkit-Roden-v1.0.0-source.zip: complete solution/source, resources/assets, build/packaging scripts, tests, developer documentation and recorded validation output.

New: consistent product identity, embedded logo/icon, clean portable root, optional per-user shortcut, safe selected-app uninstall and official Notepad++ MSI/hash profile. Configuration remains adjacent to the renamed EXE; elevation resolves the running image dynamically. Existing internal log/pipe/servicing namespace names are intentionally retained for coordination compatibility.

No installer or automatic shortcut creation is introduced. Ordinary launch stays unelevated and console-free. Existing interactive local GPUpdate is an explicit visible-console action. The unsigned release was built/host-tested; Windows GUI/UAC/install/uninstall acceptance remains required.

Final unsigned EXE SHA-256:

```text
598828c86f90e17fa27cf4442de9c5fce6656386938540738028f310799ad199
```
