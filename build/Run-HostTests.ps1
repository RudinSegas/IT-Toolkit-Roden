# Cross-platform development tests. PowerShell is only the test host, never the product GUI runtime.
param([string]$TestAssembly=(Join-Path (Split-Path $PSScriptRoot) 'artifacts/tests/Toolkit.Tests.exe'))
$ErrorActionPreference='Stop'
$assembly=[IO.Path]::GetFullPath($TestAssembly)
[void][Reflection.Assembly]::LoadFrom($assembly)
[Toolkit.Tests.Suite]::RunCore()
$literal=$assembly.Replace("'","''")
$fixtureCode="[void][Reflection.Assembly]::LoadFrom('$literal'); [Toolkit.Tests.ChildFixture]::Run()"
$hostExe=Join-Path $PSHOME $(if($IsWindows){'pwsh.exe'}else{'pwsh'})
[Toolkit.Tests.Suite]::RunProcessFixture($hostExe,[string[]]@('-NoProfile','-NonInteractive','-Command',$fixtureCode))
$sleepCode="[void][Reflection.Assembly]::LoadFrom('$literal'); [Toolkit.Tests.ChildFixture]::Sleep()"
[Toolkit.Tests.Suite]::RunTimeoutFixture($hostExe,[string[]]@('-NoProfile','-NonInteractive','-Command',$sleepCode))

[Toolkit.Tests.Suite]::RunCancellationFixture($hostExe,[string[]]@('-NoProfile','-NonInteractive','-Command',$sleepCode))
