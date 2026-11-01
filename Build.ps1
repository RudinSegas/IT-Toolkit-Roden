# Windows source build. This script is a development tool, never a GUI/runtime prerequisite.
[CmdletBinding()]
param([string]$MSBuildPath='', [switch]$RunWindowsFixtures, [string]$SigningThumbprint='', [string]$SignToolPath='', [string]$TimestampUrl='')
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'Use Windows MSBuild and the .NET Framework 4.8 Developer Pack. See build/Compile-Cross.ps1 for the optional development-host compiler.'}
if(-not $MSBuildPath){
 $command=Get-Command MSBuild.exe -ErrorAction SilentlyContinue
 if($command){$MSBuildPath=$command.Source}
 else {
  $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
  if(Test-Path -LiteralPath $vswhere){$MSBuildPath=@(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)[0]}
 }
}
if(-not $MSBuildPath -or -not(Test-Path -LiteralPath $MSBuildPath -PathType Leaf)){throw 'MSBuild not found. Install Visual Studio Build Tools with .NET desktop build tools and the .NET Framework 4.8 Developer Pack, or supply -MSBuildPath.'}
& $MSBuildPath (Join-Path $root 'IT-Toolkit-Roden.sln') /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /nologo
if($LASTEXITCODE -ne 0){throw 'MSBuild failed. Review compiler output.'}
$exe=Join-Path $root 'artifacts/portable/IT-Toolkit-Roden.exe'
$test=Join-Path $root 'artifacts/tests/Toolkit.Tests.exe'
& $test --process
if($LASTEXITCODE -ne 0){throw 'Core tests failed.'}
if($RunWindowsFixtures){& $test --windows;if($LASTEXITCODE -ne 0){throw 'Windows read-only fixtures failed.'}}
if($SigningThumbprint){
 if(-not $SignToolPath -or -not(Test-Path -LiteralPath $SignToolPath)){throw 'Supply the Windows SDK SignTool path for signing.'}
 if($SigningThumbprint -notmatch '^[A-Fa-f0-9]{40}$'){throw 'Invalid signing certificate thumbprint.'}
 $signArgs=@('sign','/sha1',$SigningThumbprint,'/fd','SHA256')
 if($TimestampUrl){if($TimestampUrl -notmatch '^https://'){throw 'Use an approved HTTPS timestamp service.'};$signArgs+=@('/tr',$TimestampUrl,'/td','SHA256')}
 & $SignToolPath @signArgs $exe
 if($LASTEXITCODE -ne 0){throw 'Authenticode signing failed.'}
 & $SignToolPath verify /pa $exe
 if($LASTEXITCODE -ne 0){throw 'Signed executable trust verification failed.'}
}
& (Join-Path $root 'build/Inspect-Resources.ps1') -Executable $exe
& (Join-Path $root 'build/Package-Release.ps1')
Write-Output ('Built portable application: '+$exe)
Write-Output 'Windows operational acceptance is separate from compilation and core/read-only fixtures.'
