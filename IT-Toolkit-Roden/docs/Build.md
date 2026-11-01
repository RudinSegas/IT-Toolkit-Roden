# Build and package — v1.0.0

## Windows development prerequisites

Visual Studio / Build Tools with .NET desktop build tools, MSBuild and the .NET Framework 4.8 Developer/Targeting Pack. Framework assemblies only; no NuGet restore, third-party runtime or PowerShell 7 is required by the product.

From a Developer Command Prompt in the source root:

```bat
MSBuild.exe IT-Toolkit-Roden.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m
artifacts\tests\Toolkit.Tests.exe --process --windows
```

Or, where existing development policy permits:

```powershell
.\Build.ps1 -RunWindowsFixtures
```

Build.ps1 rebuilds production/tests, runs core/process tests and optional read-only Windows fixtures, inspects embedded resources and packages both ZIPs. Optional -SigningThumbprint/-SignToolPath/-TimestampUrl use an existing certificate-store key; no private key is exported. This supplied build is unsigned.

`build/Package-Release.ps1 -OutputDirectory <folder>` creates IT-Toolkit-Roden-v1.0.0-portable.zip and IT-Toolkit-Roden-v1.0.0-source.zip. It copies an explicit runtime whitelist, embeds end-user docs in docs, marks the config Hidden (and sets the ZIP DOS Hidden bit), records checksums and excludes developer files from the portable folder. Extraction tools may ignore Hidden; CLR loading is unaffected.

Do not change execution policy to run build scripts. MSBuild is the alternative for restricted development hosts. Ordinary GUI startup never invokes these scripts.

## Outputs and resources

- artifacts/portable/IT-Toolkit-Roden.exe and IT-Toolkit-Roden.exe.config
- artifacts/tests/Toolkit.Tests.exe and its config
- assets/IT-Toolkit-Roden.ico (16, 24, 32, 48, 64, 128, 256 pixels)
- Embedded Toolkit.MainWindow.xaml, Toolkit.Theme.xaml, Toolkit.App.config, Toolkit.Icon and Toolkit.Logo

Both App and Tests projects compile the actual production sources. XAML is embedded XML loaded from the trusted assembly; no loose XAML or BAML build is required. ApplicationIcon adds the native icon in Windows MSBuild; the cross compiler passes the same ICO to Roslyn Win32 resources. Do not move the adjacent config or add loose runtime DLLs.

## Host cross compilation used for this release

Linux x64 cannot execute WPF. Roslyn hosted by development-only PowerShell 7.4.6 compiles C#5 against official Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3 reference assemblies; these tools are not bundled or required at runtime.

```powershell
.\build\Compile-Cross.ps1 -FrameworkReferences <net48-directory> -RoslynDirectory <roslyn-directory>
.\build\Compile-Cross.ps1 -FrameworkReferences <net48-directory> -RoslynDirectory <roslyn-directory> -Tests
.\build\Run-HostTests.ps1
.\build\Inspect-Resources.ps1
```

`python tests/audit_source.py` statically checks XAML/source/version/template contracts; `python tests/inspect_pe.py <exe>` inspects GUI/asInvoker/icon resources without executing Windows. Python is a development audit tool only. Fixing compiler failures may require a focused correction; final test results belong in docs/Test-Report.md and validation/.

Deterministic compilation requires identical compiler/inputs/paths; different Windows toolchains or signing can change file hashes. Cross build and static resource checks are not actual WPF layout, UAC or installer validation.
