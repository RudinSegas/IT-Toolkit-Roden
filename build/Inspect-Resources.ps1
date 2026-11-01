param([string]$Executable=(Join-Path (Split-Path $PSScriptRoot) 'artifacts/portable/IT-Toolkit-Roden.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$assembly=[Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($Executable))
if($assembly.GetName().Name -ne 'IT-Toolkit-Roden' -or $assembly.GetName().Version.ToString() -ne '1.0.0.0'){throw 'Unexpected executable identity/version.'}
$product=$assembly.GetCustomAttributes([Reflection.AssemblyProductAttribute],$false)[0].Product
if($product -ne 'IT Toolkit by Roden Segas'){throw 'Unexpected product metadata.'}
Write-Output 'PASS: renamed assembly identity 1.0.0.0 and product metadata'
$files=@{'MainWindow.xaml'='src/Toolkit.App/MainWindow.xaml';'Theme.xaml'='src/Toolkit.App/Theme.xaml';'App.config'='src/Toolkit.App/App.config';'Icon'='assets/IT-Toolkit-Roden.ico';'Logo'='assets/IT-Toolkit-Roden-logo-small.png'}
foreach($name in $files.Keys) {
 $stream=$assembly.GetManifestResourceStream('Toolkit.'+$name)
 if(-not $stream){throw ('Missing resource '+$name)}
 try {
  $memory=New-Object IO.MemoryStream
  $stream.CopyTo($memory)
  $embedded=[Convert]::ToBase64String($memory.ToArray())
  $source=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $root $files[$name])))
  if($embedded -ne $source){throw ('Embedded resource differs from source: '+$name)}
  if($name -eq 'App.config' -and $embedded -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($Executable+'.config'))){throw 'Distributed runtime config differs from embedded config.'}
  Write-Output ('PASS: embedded '+$name+' matches source'+$(if($name -eq 'App.config'){' and distributed config'}else{''}))
 } finally {$stream.Dispose();if($memory){$memory.Dispose()}}
}
