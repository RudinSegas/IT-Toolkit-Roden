# Notepad++ installer correction

## Source finding

The supplied internal v3.1.2 direct profile only selected `npp.<version>.Installer.x64.exe`, despite the official stable release also publishing an x64 MSI. Signature.VerifyPublisher validates the default WinVerifyTrust signature and then reads a certificate with X509Certificate.CreateFromSignedFile; it does not select/validate a specific secondary Authenticode signature. Official Notepad++ notes describe the EXE's legacy self-signed plus commercial signature and the MSI's commercial-only signature. This creates a concrete trust-selection compatibility risk in the existing EXE path. It is a source-level finding; no exact user Windows exception/log was supplied and no Windows reproduction is claimed.

## Targeted correction

The Notepad++ profile now selects only the stable official repository's x64 MSI. It requires a valid asset SHA-256, consistent release tag/filename/version, expected NOTEPAD++ publisher and full Windows trust verification. It never imports a self-signed root, ignores an invalid signature, or falls back to an EXE after verification failure. Standard msiexec /i arguments are /qn /norestart REBOOT=ReallySuppress; WinGet's explicit Notepad++ installer family is wix. Shared precheck/version-skip, native exit/reboot reporting, fresh inventory/post-version verification, downloads/retries and cleanup remain.

No fixed release version/download URL/hash is embedded from the research. Runtime metadata determines them; absent/changed/ambiguous metadata fails closed with the stage and reason. Official-source inspection is not live installation testing.

## References inspected 1 November 2026

- Official stable release and asset hashes: https://github.com/notepad-plus-plus/notepad-plus-plus/releases/latest
- Official community certificate clarification: https://community.notepad-plus-plus.org/topic/27252/sha512-certificate-cannot-be-validated
- Official security FAQ and expected commercial signer: https://community.notepad-plus-plus.org/topic/27388/faq-february-security-announcement
- Windows Installer command options: https://learn.microsoft.com/en-us/windows/win32/msi/standard-installer-command-line-options

Real Windows installation, trust/revocation behavior and independent post-install acceptance remain unexecuted in this environment. See Windows-Validation.md.
