# Packages only explicitly selected runtime files; never copies a development tree into the portable release.
param([string]$OutputDirectory='')
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'artifacts'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$exe=Join-Path $root 'artifacts/portable/IT-Toolkit-Roden.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Build the release first.'}
$config=$exe+'.config'
if(-not(Test-Path -LiteralPath $config)){throw 'Runtime configuration is missing.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stage=Join-Path $root ('artifacts/package-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
function Archive([string]$folder,[string]$zip){
 if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
}
try {
 $portableStage=Join-Path $stage 'portable';$portable=Join-Path $portableStage 'IT-Toolkit-Roden'
 [void][IO.Directory]::CreateDirectory((Join-Path $portable 'docs'));[void][IO.Directory]::CreateDirectory((Join-Path $portable 'assets'))
 Copy-Item -LiteralPath $exe -Destination $portable
 Copy-Item -LiteralPath $config -Destination $portable
 foreach($name in 'README.md','START_HERE.md','CHANGELOG.md','LICENSE','OWNERSHIP.md','THIRD-PARTY-NOTICES.md'){
  Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $portable ('docs/'+$name))
 }
 foreach($doc in Get-ChildItem -LiteralPath (Join-Path $root 'docs') -File){Copy-Item -LiteralPath $doc.FullName -Destination (Join-Path $portable 'docs')}
 foreach($asset in 'IT-Toolkit-Roden.ico','IT-Toolkit-Roden-logo.png'){Copy-Item -LiteralPath (Join-Path $root ('assets/'+$asset)) -Destination (Join-Path $portable 'assets')}
 $readme=Join-Path $portable 'docs/README.md';[IO.File]::WriteAllText($readme,([IO.File]::ReadAllText($readme).Replace('](assets/','](../assets/').Replace('src="assets/','src="../assets/').Replace('](docs/','](')),(New-Object Text.UTF8Encoding($false)))
 $hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText((Join-Path $portable 'docs/IT-Toolkit-Roden.exe.sha256'),$hash+'  IT-Toolkit-Roden.exe'+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
 if([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT){[IO.File]::SetAttributes((Join-Path $portable 'IT-Toolkit-Roden.exe.config'),[IO.FileAttributes]::Hidden)}
 $portableZip=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'IT-Toolkit-Roden-v1.0.0-portable.zip'
 Archive $portableStage $portableZip
 # ZIP DOS Hidden attribute, also on non-Windows build hosts. Extraction tools may ignore attributes.
 $archive=[IO.Compression.ZipFile]::Open($portableZip,[IO.Compression.ZipArchiveMode]::Update)
 try{$entry=$archive.GetEntry('IT-Toolkit-Roden/IT-Toolkit-Roden.exe.config');$entry.ExternalAttributes=2}finally{$archive.Dispose()}
 $sourceStage=Join-Path $stage 'developer';$source=Join-Path $sourceStage 'IT-Toolkit-Roden-source'
 [void][IO.Directory]::CreateDirectory($source)
 foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force){
  $relative=$file.FullName.Substring($root.Length).TrimStart('\','/')
  if($relative -match '^(?:artifacts|\.git)(?:[\\/]|$)' -or $relative -match '(?:^|[\\/])(?:bin|obj|__pycache__)(?:[\\/]|$)'){continue}
  if($file.Extension -in '.pfx','.p12','.key','.snk'){throw 'Private key material must not be packaged.'}
  $destination=Join-Path $source $relative;[void][IO.Directory]::CreateDirectory((Split-Path $destination));Copy-Item -LiteralPath $file.FullName -Destination $destination
 }
 [void][IO.Directory]::CreateDirectory((Join-Path $source 'validation'))
 foreach($name in 'host-test-output.txt','static-audit.txt','pe-inspection.json','resource-inspection.txt','build-output.txt'){
  $path=Join-Path $root ('artifacts/'+$name);if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $source 'validation')}
 }
 foreach($name in 'Toolkit.Tests.exe','Toolkit.Tests.exe.config'){$path=Join-Path $root ('artifacts/tests/'+$name);if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $source 'validation')}}
 foreach($folder in $portable,$source){
  $lines=foreach($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Force){(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$file.FullName.Substring($folder.Length+1).Replace('\','/')}
  [IO.File]::WriteAllLines((Join-Path $folder 'docs/SHA256SUMS.txt'),[string[]]$lines,(New-Object Text.UTF8Encoding($false)))
 }
 # Recreate portable archive after its documentation checksum manifest is complete.
 Archive $portableStage $portableZip
 $archive=[IO.Compression.ZipFile]::Open($portableZip,[IO.Compression.ZipArchiveMode]::Update)
 try{$archive.GetEntry('IT-Toolkit-Roden/IT-Toolkit-Roden.exe.config').ExternalAttributes=2}finally{$archive.Dispose()}
 $sourceZip=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'IT-Toolkit-Roden-v1.0.0-source.zip'
 Archive $sourceStage $sourceZip
 Write-Output ('Portable: '+$portableZip);Write-Output ('Source: '+$sourceZip);Write-Output ('EXE SHA-256: '+$hash)
} finally {if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
