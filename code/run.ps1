param([string]$SdkPath,[string]$RuntimeDirectory,[string]$ModelsDirectory,[ValidateSet('Ocr','Vision')][string]$InputMode='Ocr',[switch]$NoModel)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../tools/Model-Helpers.ps1"
if(Get-CimInstance Win32_Process -Filter "Name='BigBlueFish.exe' OR Name='VPet-Simulator.Windows.exe'"){
    throw '已有桌宠实例运行。请从其托盘菜单退出后再启动源码版。'
}
& "$PSScriptRoot/build.ps1" -SdkPath $SdkPath -Locked -InputMode $InputMode
$tfm=if($InputMode -eq 'Ocr'){'net10.0-windows10.0.26100.0'}else{'net10.0-windows7.0'}
$directory=Join-Path $PSScriptRoot "BigBlueFish.Windows/bin/x64/Release/$tfm"
if(!$RuntimeDirectory){$RuntimeDirectory=$env:BLUE_WHALE_RUNTIME}
if(!$ModelsDirectory){$ModelsDirectory=$env:BLUE_WHALE_MODELS}
if(!$RuntimeDirectory){$RuntimeDirectory='C:\Program Files\蓝色大肥鱼\runtime'}
if(!$ModelsDirectory){$ModelsDirectory='C:\Program Files\蓝色大肥鱼\models'}
$ModelsDirectory=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ModelsDirectory)
$selection=Resolve-DevelopmentModel -ModelsDirectory $ModelsDirectory -NoModel:$NoModel
if($selection.Source -ne 'none'){
    if(!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'llama-server.exe'))){throw '推理运行时目录缺少 llama-server.exe，请指定 -RuntimeDirectory。'}
    Set-DevelopmentJunction (Join-Path $directory 'runtime') $RuntimeDirectory
    Set-DevelopmentJunction (Join-Path $directory 'models') $ModelsDirectory
}
Save-ModelSelection $directory $selection
Write-Host $(if($selection.Source -eq 'none'){'以无模型模式启动。'}else{"使用本地模型：$($selection.FileName)"})
Start-Process -FilePath (Join-Path $directory 'BigBlueFish.exe') -WorkingDirectory $directory -WindowStyle Hidden
