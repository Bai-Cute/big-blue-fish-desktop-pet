# These transport fixtures exercise scripts and GGUF headers, not model inference.
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. "$root/tools/Model-Helpers.ps1"
$area=Join-Path $root ('.work/model-script-tests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($area) | Out-Null
$script:checks=0
function Assert($Condition,[string]$Message){if(!$Condition){throw $Message};$script:checks++}
function Expect-Failure([scriptblock]$Action,[string]$Message){
    $failed=$false
    try { & $Action | Out-Null } catch { $failed=$true }
    Assert $failed $Message
}
function Fixture([string]$Path){
    [IO.Directory]::CreateDirectory((Split-Path $Path -Parent)) | Out-Null
    $writer=[IO.BinaryWriter]::new([IO.File]::Create($Path))
    try {$writer.Write([uint32]0x46554747);$writer.Write([uint32]3);$writer.Write([uint64]1);$writer.Write([uint64]1)}
    finally {$writer.Dispose()}
}
$sources=Get-ModelSources
$production=Get-Content -LiteralPath "$root/deployment/ModelStore.cs" -Raw
foreach($source in $sources){
    foreach($field in @('Id','Label','FileName','Url','Page','Sha256')){
        Assert ($production.Contains('"'+$source.$field+'"')) "Production source differs: $field"
    }
}
foreach($source in $sources){
    $models=Join-Path $area ($source.Id+'/models')
    Fixture (Join-Path $models $source.FileName)
    Assert ((Resolve-DevelopmentModel $models).FileName -ceq $source.FileName) 'Source model not recognized'
}
$manual=Join-Path $area '手动来源/自选模型.gguf'
Fixture $manual
$install=Join-Path $area 'manual'
$dest=Join-Path $install 'data'
& "$root/tools/Setup-Model.ps1" -ModelFile $manual -DestinationDirectory $install -AllowUnverifiedModel
$selection=Read-ModelSelection $dest
Assert ($selection.Source -eq 'manual' -and $selection.FileName -eq '自选模型.gguf') 'Actual manual filename lost'
Assert ((Get-FileHash $manual).Hash -eq (Get-FileHash (Join-Path $dest 'models/自选模型.gguf')).Hash) 'Import changed bytes'
Assert ((Resolve-DevelopmentModel (Join-Path $dest 'models')).FileName -eq '自选模型.gguf') 'Selection not honored'
Assert ((Resolve-DevelopmentModel ((Join-Path $dest 'models')+'\')).FileName -eq '自选模型.gguf') 'Trailing separator lost selection'
$before=(Get-FileHash (Join-Path $dest 'model-selection.json')).Hash
function Read-Host { return 'N' }
Expect-Failure { & "$root/tools/Setup-Model.ps1" -Source Manual -ModelFile $manual -DestinationDirectory $install } 'Declined manual import should stop'
Assert ((Get-FileHash (Join-Path $dest 'model-selection.json')).Hash -eq $before) 'Decline altered selection'
$invalid=Join-Path $area 'invalid.gguf'
[IO.File]::WriteAllText($invalid,'not a GGUF model')
Expect-Failure { & "$root/tools/Setup-Model.ps1" -ModelFile $invalid -DestinationDirectory $install -AllowUnverifiedModel } 'Non-GGUF accepted'
Assert ((Get-FileHash (Join-Path $dest 'model-selection.json')).Hash -eq $before) 'Invalid import altered selection'
# Reimport in place and replace an existing file, exercising atomic paths.
& "$root/tools/Setup-Model.ps1" -ModelFile (Join-Path $dest 'models/自选模型.gguf') -DestinationDirectory $install -AllowUnverifiedModel
& "$root/tools/Setup-Model.ps1" -ModelFile $manual -DestinationDirectory $install -AllowUnverifiedModel
Assert ((Resolve-DevelopmentModel (Join-Path $dest 'models')).Source -eq 'manual') 'Reimport failed'
& "$root/tools/Setup-Model.ps1" -Source None -DestinationDirectory $install
Assert ((Resolve-DevelopmentModel (Join-Path $dest 'models')).Source -ceq 'none') 'None ignored existing model'
Assert (Test-Path (Join-Path $dest 'models/自选模型.gguf')) 'None removed model'
Assert ((Resolve-DevelopmentModel (Join-Path $area 'nonexistent/models') -NoModel).Source -eq 'none') 'NoModel requires files'
Save-ModelSelection $dest ([ordered]@{Source='manual';FileName='missing.gguf';Sha256=$null})
Fixture (Join-Path $dest ('models/'+$sources[0].FileName))
Expect-Failure { Resolve-DevelopmentModel (Join-Path $dest 'models') } 'Missing explicit selection fell back'
Save-ModelSelection $dest ([ordered]@{Source='manual';FileName='../invalid.gguf';Sha256=$null})
Expect-Failure { Resolve-DevelopmentModel (Join-Path $dest 'models') } 'Non-basename accepted'
$alias=Join-Path $area 'alias/models'
Fixture (Join-Path $alias 'Qwen3.5-4B-heretic-f16_Q4_K_M.gguf')
Assert ((Resolve-DevelopmentModel $alias).FileName -eq 'Qwen3.5-4B-heretic-f16_Q4_K_M.gguf') 'Upstream alias lost'
$link=Join-Path $area 'link'
Set-DevelopmentJunction $link (Split-Path $manual -Parent)
Set-DevelopmentJunction $link (Split-Path $manual -Parent)
Set-DevelopmentJunction $link $alias
Assert (Test-Path $manual) 'Retarget deleted original model'
Assert (Test-Path (Join-Path $link 'Qwen3.5-4B-heretic-f16_Q4_K_M.gguf')) 'Retarget did not use new directory'
Expect-Failure { Set-DevelopmentJunction $alias $area } 'Real directory should not be replaced'
# Mock only network transport and the expected download hash. Real fixture hashes were checked above.
$global:BigBlueFishModelScriptTransport=@{Requests=[Collections.Generic.List[string]]::new();ExpectedHash=''}
function Invoke-WebRequest($Uri,$OutFile){$global:BigBlueFishModelScriptTransport.Requests.Add($Uri);Fixture $OutFile}
function Get-FileHash($LiteralPath,$Algorithm){[pscustomobject]@{Hash=$global:BigBlueFishModelScriptTransport.ExpectedHash}}
foreach($choice in @('HuggingFace','ModelScope')){
    $index=if($choice -eq 'HuggingFace'){0}else{1}
    $global:BigBlueFishModelScriptTransport.ExpectedHash=$sources[$index].Sha256
    $download=Join-Path $area ('download-'+$choice)
    & "$root/tools/Setup-Model.ps1" -Source $choice -DestinationDirectory $download
    & "$root/tools/Setup-Model.ps1" -Source $choice -DestinationDirectory $download
    Assert ((Read-ModelSelection (Join-Path $download 'data')).Source -eq $sources[$index].Id) 'Wrong downloaded selection'
    Assert ((Get-ChildItem (Join-Path $download 'data/models') -File).Count -eq 1) 'Extra artifact downloaded'
}
Assert ($global:BigBlueFishModelScriptTransport.Requests.Count -eq 2) 'Unexpected download count or cached model redownload'
Assert ($global:BigBlueFishModelScriptTransport.Requests[0] -eq $sources[0].Url -and $global:BigBlueFishModelScriptTransport.Requests[1] -eq $sources[1].Url) 'Wrong download endpoints'
function Invoke-WebRequest($Uri,$OutFile){throw 'Injected connection failure'}
$failedDest=Join-Path $area 'download-failure'
Save-ModelSelection (Join-Path $failedDest 'data') ([ordered]@{Source='none';FileName=$null;Sha256=$null})
Expect-Failure { & "$root/tools/Setup-Model.ps1" -DestinationDirectory $failedDest } 'Download error swallowed'
Assert ((Read-ModelSelection (Join-Path $failedDest 'data')).Source -eq 'none') 'Failed download altered selection'
function Invoke-WebRequest($Uri,$OutFile){Fixture $OutFile}
$global:BigBlueFishModelScriptTransport.ExpectedHash='0'*64
Expect-Failure { & "$root/tools/Setup-Model.ps1" -DestinationDirectory $failedDest } 'Wrong download hash accepted'
Assert ((Read-ModelSelection (Join-Path $failedDest 'data')).Source -eq 'none') 'Hash failure altered selection'
# Exercise the actual launcher in an isolated source tree; build, process inventory and launch are substituted.
$launcher=Join-Path $area 'launcher'
[IO.Directory]::CreateDirectory((Join-Path $launcher 'code')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $launcher 'tools')) | Out-Null
Copy-Item -LiteralPath "$root/code/run.ps1" -Destination (Join-Path $launcher 'code/run.ps1')
Copy-Item -LiteralPath "$root/tools/Model-Helpers.ps1" -Destination (Join-Path $launcher 'tools/Model-Helpers.ps1')
Set-Content -LiteralPath (Join-Path $launcher 'code/build.ps1') -Value @'
param($SdkPath,[switch]$Locked,$InputMode)
$global:BigBlueFishModelScriptTransport.Builds++
$out=Join-Path $PSScriptRoot 'BigBlueFish.Windows/bin/x64/Release/net10.0-windows10.0.26100.0'
[IO.Directory]::CreateDirectory($out) | Out-Null
'@ -Encoding utf8
$global:BigBlueFishModelScriptTransport.Builds=0
$global:BigBlueFishModelScriptTransport.Launched=$null
$global:BigBlueFishModelScriptTransport.Running=$false
function Get-CimInstance { if($global:BigBlueFishModelScriptTransport.Running){[pscustomobject]@{ProcessId=123}} }
function Start-Process($FilePath,$WorkingDirectory,$WindowStyle){$global:BigBlueFishModelScriptTransport.Launched=$WorkingDirectory}
$run=Join-Path $launcher 'code/run.ps1'
$out=Join-Path $launcher 'code/BigBlueFish.Windows/bin/x64/Release/net10.0-windows10.0.26100.0'
& $run -NoModel -ModelsDirectory (Join-Path $area 'absent/models') -RuntimeDirectory (Join-Path $area 'absent/runtime')
Assert ((Read-ModelSelection (Join-Path $out 'data')).Source -eq 'none') 'Launcher failed NoModel'
Assert ($global:BigBlueFishModelScriptTransport.Launched -eq $out) 'Launcher used wrong directory'
$runtime=Join-Path $area 'runtime'
[IO.Directory]::CreateDirectory($runtime) | Out-Null
[IO.File]::WriteAllText((Join-Path $runtime 'llama-server.exe'),'fixture')
& $run -ModelsDirectory (Join-Path $area 'modelscope/models') -RuntimeDirectory $runtime
Assert ((Read-ModelSelection (Join-Path $out 'data')).FileName -eq $sources[1].FileName) 'Launcher rejected domestic filename'
Save-ModelSelection $dest ([ordered]@{Source='manual';FileName='自选模型.gguf';Sha256=$null})
& $run -ModelsDirectory (Join-Path $dest 'models') -RuntimeDirectory $runtime
Assert ((Read-ModelSelection (Join-Path $out 'data')).FileName -eq '自选模型.gguf') 'Launcher rejected actual manual name'
Assert (Test-Path $manual) 'Launcher modified source model'
Assert ((Read-ModelSelection $dest).Source -eq 'manual') 'Launcher rewrote source selection'
$global:BigBlueFishModelScriptTransport.Running=$true
$buildCount=$global:BigBlueFishModelScriptTransport.Builds
Expect-Failure { & $run -NoModel } 'Launcher ignored running app'
Assert ($global:BigBlueFishModelScriptTransport.Builds -eq $buildCount) 'Launcher built before instance check'
Write-Host "Model script checks passed: $script:checks"

Remove-Variable BigBlueFishModelScriptTransport -Scope Global
