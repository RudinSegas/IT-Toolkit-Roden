# Development-host compiler only. No PowerShell/Roslyn is used by the resulting application.
param([Parameter(Mandatory)][string]$FrameworkReferences,[Parameter(Mandatory)][string]$RoslynDirectory,[switch]$Tests)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
Add-Type -Path (Join-Path $RoslynDirectory 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $RoslynDirectory 'Microsoft.CodeAnalysis.CSharp.dll')
$trees=New-Object 'Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]'
$parse=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp5)
$files=@(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs)
if($Tests){$files+=@(Get-ChildItem (Join-Path $root 'tests') -Filter *.cs)}
foreach($f in $files){$trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($f.FullName),$parse,$f.FullName))}
$refs=New-Object 'Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
foreach($n in 'mscorlib','System','System.Core','System.Xml','System.Runtime.Serialization','System.Management','System.Net.Http','System.Security','System.Web.Extensions','WindowsBase','PresentationCore','PresentationFramework','System.Xaml'){
 $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $FrameworkReferences ($n+'.dll'))))
}
$kind=if($Tests){[Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication}else{[Microsoft.CodeAnalysis.OutputKind]::WindowsApplication}
$settings=(New-Object Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions($kind)).WithPlatform([Microsoft.CodeAnalysis.Platform]::X64).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
$settings=$settings.WithMainTypeName($(if($Tests){'Toolkit.Tests.Suite'}else{'Toolkit.App.Entry'}))
$name=if($Tests){'Toolkit.Tests'}else{'IT-Toolkit-Roden'}
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($name,$trees,$refs,$settings)
$resources=New-Object 'Collections.Generic.List[Microsoft.CodeAnalysis.ResourceDescription]'
foreach($n in 'MainWindow.xaml','Theme.xaml','App.config'){
 $resourcePath=Join-Path $root ('src/Toolkit.App/'+$n)
 $provider={return [IO.File]::OpenRead($resourcePath)}.GetNewClosure()
 $resources.Add([Microsoft.CodeAnalysis.ResourceDescription]::new(('Toolkit.'+$n),[Func[IO.Stream]]$provider,$true))
}
foreach($asset in @(@('Logo','IT-Toolkit-Roden-logo-small.png'),@('Icon','IT-Toolkit-Roden.ico'))){
 $assetPath=Join-Path $root ('assets/'+$asset[1])
 $provider={return [IO.File]::OpenRead($assetPath)}.GetNewClosure()
 $resources.Add([Microsoft.CodeAnalysis.ResourceDescription]::new(('Toolkit.'+$asset[0]),[Func[IO.Stream]]$provider,$true))
}
$dest=Join-Path $root ('artifacts/'+$(if($Tests){'tests'}else{'portable'}));[void][IO.Directory]::CreateDirectory($dest)
$manifest=[IO.File]::OpenRead((Join-Path $root 'src/Toolkit.App/app.manifest'))
$icon=[IO.File]::OpenRead((Join-Path $root 'assets/IT-Toolkit-Roden.ico'))
$native=$compilation.CreateDefaultWin32Resources($true,$false,$manifest,$icon)
$output=[IO.File]::Create((Join-Path $dest ($name+'.exe')))
try{
 $result=$compilation.Emit($output,$null,$null,$native,$resources)
 foreach($d in $result.Diagnostics){if($d.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden){$d.ToString()}}
 if(-not$result.Success){throw 'Compilation failed.'}
}finally{$output.Dispose();$native.Dispose();$manifest.Dispose();$icon.Dispose()}
Copy-Item (Join-Path $root 'src/Toolkit.App/App.config') (Join-Path $dest ($name+'.exe.config')) -Force
'Built '+(Join-Path $dest ($name+'.exe'))
