param([string]$SdkPath,[string]$RuntimeDirectory,[string]$ModelsDirectory,[ValidateSet('Ocr','Vision')][string]$InputMode='Ocr')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/build.ps1" -SdkPath $SdkPath -Locked -InputMode $InputMode
$tfm=if($InputMode -eq 'Ocr'){'net10.0-windows10.0.26100.0'}else{'net10.0-windows7.0'}
$directory=Join-Path $PSScriptRoot "VPet-Simulator.Windows/bin/x64/Release/$tfm"
if(!$RuntimeDirectory){$RuntimeDirectory=$env:BLUE_WHALE_RUNTIME}
if(!$ModelsDirectory){$ModelsDirectory=$env:BLUE_WHALE_MODELS}
if(!$RuntimeDirectory){$RuntimeDirectory='D:\Program Files\蓝色大肥鱼\runtime'}
if(!$ModelsDirectory){$ModelsDirectory='D:\Program Files\蓝色大肥鱼\models'}
foreach($item in @(@('runtime',$RuntimeDirectory,'llama-server.exe'),@('models',$ModelsDirectory,'Qwen3.5-4B-heretic-Q4_K_M.gguf'))){
    if(!(Test-Path -LiteralPath (Join-Path $item[1] $item[2]))){throw "缺少 $($item[2])，请指定正确的目录。"}
    $destination=Join-Path $directory $item[0]
    if(!(Test-Path -LiteralPath $destination)){
        New-Item -ItemType Junction -Path $destination -Target (Resolve-Path -LiteralPath $item[1]).Path | Out-Null
    }
}
if(Get-CimInstance Win32_Process -Filter "Name='VPet-Simulator.Windows.exe'"){
    throw '已有桌宠实例运行。请从其托盘菜单退出后再启动源码版。'
}
Start-Process -FilePath (Join-Path $directory 'VPet-Simulator.Windows.exe') -WorkingDirectory $directory -WindowStyle Hidden
