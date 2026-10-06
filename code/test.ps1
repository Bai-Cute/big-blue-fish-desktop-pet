param([string]$SdkPath,[string]$InstalledDirectory='D:\Program Files\蓝色大肥鱼',[ValidateSet('Ocr','Vision')][string]$InputMode='Ocr')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    if(Get-CimInstance Win32_Process -Filter "Name='VPet-Simulator.Windows.exe'"){throw '请先从托盘菜单退出桌宠，再执行实窗测试。'}
    $sdk=& "$PSScriptRoot/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
    $sandbox=Join-Path $PSScriptRoot '.verification'
    New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
    foreach($name in @('assets','runtime','models')){
        $target=if($name -eq 'assets'){Join-Path $PSScriptRoot 'VPet-Simulator.Windows/assets'}else{Join-Path $InstalledDirectory $name}
        $link=Join-Path $sandbox $name
        if(!(Test-Path -LiteralPath $link)){New-Item -ItemType Junction -Path $link -Target $target | Out-Null}
    }
    & $sdk run --project "$PSScriptRoot/tests/RecoveryVerification/RecoveryVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- $PSScriptRoot $InstalledDirectory
    if($LASTEXITCODE -ne 0){throw '验证失败，见上方诊断'}
} finally {Pop-Location}
