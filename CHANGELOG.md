# Changelog

## 1.0.0 — Initial Public Release — 2026-11-01

Public numbering starts at 1.0.0; this release continues internal v3.1.2.

- Renamed product, executable/config, assembly and package identity to IT Toolkit by Roden Segas / IT-Toolkit-Roden, with 1.0.0 metadata and current reports.
- Added an original SVG/PNG/multi-resolution ICO; embedded executable/window/taskbar icon and compact Dashboard/About branding.
- Clean portable folder: EXE, adjacent config (Hidden ZIP attribute), docs and assets. Source/tests/build tools are delivered separately. Added an explicit current-user desktop shortcut action.
- Added selected-app interactive uninstall with exact registry identity rechecks, conservative component/command/path/trust protections, no generic privileged endpoint, duplicate guard and independent post-inventory reporting.
- Notepad++ now resolves the official stable x64 MSI, requires its published SHA-256 and trusted NOTEPAD++ publisher, and uses standard no-restart MSI arguments. Avoids the legacy dual-signed EXE selection path; actual user failure was not reproduced without a Windows log/runtime.
- Retained existing WPF/Framework 4.8 architecture, controls and working modules. Added targeted release/uninstall/installer tests and updated English portfolio documentation.
- Build and host results are separate from unexecuted Windows acceptance. See Test-Report.md.

## Internal development history

## 3.1.2 — 2026-11-01

- Automatic Advanced Diagnostics with two-minute caching, loading state, stale-result rejection and manual refresh.
- Human-readable category summaries, explicit unavailable evidence, storage cards with capacity/health/reliability/volume detail, problem-device descriptions and optional technical evidence.
- Dedicated service inventory and selected-service state-aware Start/Stop/Restart; service-name validation and protected-service exclusions preserve a typed native helper endpoint.
- Dedicated bounded Event Log browser, selected log, server-side filters, older-page cursor and untruncated selected-event message/XML.
- Replaced local native Group Policy refresh with a fixed current-user interactive Command Prompt; manual Windows prompts, no stdin redirection, no automated verification/reboot/logoff. Remote policy and GPResult retained.
- Added official Lenovo System Update / Dell Command | Update Classic download-only resolution, verified Downloads files, Explorer selection and explicit official-page fallback.
- Roden Segas ownership metadata/About/documentation; preserved prior LICENSE obligations.
- Preserved explicit OneWay bindings for read-only values, including live Progress notifications. Added core and Windows-only collection regression fixtures.
- Build/host test results are documented separately from unexecuted Windows runtime acceptance.

## 3.1.1 — 2026-11-01

- Fixed direct installer MOTW path handling using native CreateFileW + safe handle, preserving ZoneId=3 and publisher/hash validation.
- Added bounded 408/429/500/502/503/504 retries, Retry-After/exponential backoff, redirect allowlists, cancellation and exact stage/URL diagnostics.
- Separated helper result, installer native code, action verdict, verification and cleanup. Machine installer scratch is Administrators/SYSTEM-only.
- Added state-aware prechecks/skips/postchecks for installs/updates, NetFx3, services, DHCP and maintenance; missing evidence is inconclusive. Exact normalized versions include all version components.
- Replaced interactive local gpupdate.exe with native user/computer RefreshPolicyEx force requests and bounded notification/event monitoring; no prompt language parsing, stdin replies or automatic restart/logoff. Added authorized remote event evidence before/after remote scheduling.
- Redesigned Advanced Diagnostics into ten contextual categories, real evidence highlights, readable selectable grids, complete detail panel, copy/export and category scroll reset.
- Added Overview and dedicated BIOS/Hardware views, structured domain/authentication/policy evidence, service Start/Stop alongside explicit Restart, collapsible output and persisted adjustable height.
- Retained explicit OneWay read-only bindings/INotifyPropertyChanged; added HTTP/state/version/static and Windows-only MOTW/signature/category regressions.
- Compiled and tested on Linux; actual Windows runtime acceptance is pending. No repaired Windows behavior is certified by compilation alone.

## 3.1.0 — previous release, 1 November 2026

- Preserved the attached native v3.0.0 source and its corrected OneWay bindings.
- Added process lifecycle/PID messages, duplicate-command exclusion, actual read-only cancellation/exit confirmation and bounded provider calls.
- Previous GPUpdate implementation (superseded after the reported Windows prompt failure): /force /wait:120, closed input, 180-second outer timeout, cancellable launcher, no /boot or /logoff. Group Policy services may continue policy processing after the client exits.
- Added authenticated cancellation messages to the elevated helper: cancel downloads at safe checkpoints; active installers/servicing remain advisory, never forcibly killed.
- Added DNS flush, NetFx3, fixed DHCP GUID operations and three allowlisted service restarts.
- Added Advanced Diagnostics for networking, update history/scan, pending reboot, storage/SMART/BitLocker, devices/drivers/BIOS, installed software, performance/startup and domain diagnostics.
- Added official direct installer profiles and verification; WinGet becomes optional. Added explicit vendor-distribution/unsupported-metadata failures.
- Added selected-section HTML/JSON privacy reports, CSV/JSON software inventory, snapshot save/load/compare and safe CSV cells.
- Added browser-only Ookla fallback; native enterprise/redistribution authorization not established.
- Fixed page scroll reset, expanded explicit binding directions, dark diagnostic grids, and removed per-package duplicated final output enqueueing.
- Added host regression tests and Windows-only binding/scroll/progress/metadata fixtures; built portable executable and source packages.

Windows runtime/real servicing/installations remain unverified; this changelog does not certify them.
