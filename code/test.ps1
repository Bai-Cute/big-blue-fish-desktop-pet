param([string]$SdkPath,[string]$InstalledDirectory='C:\Program Files\蓝色大肥鱼',[ValidateSet('Ocr','Vision')][string]$InputMode='Ocr',[switch]$LegacyRecovery)
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
    if($LegacyRecovery){
        & $sdk run --project "$PSScriptRoot/tests/RecoveryVerification/RecoveryVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- $PSScriptRoot $InstalledDirectory
        if($LASTEXITCODE -ne 0){throw '历史恢复验证失败，见上方诊断'}
    }else{
        & $sdk run --project "$PSScriptRoot/tests/RunnerVerification/RunnerVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- --weather-cache (Join-Path $sandbox "weather-$InputMode")
        if($LASTEXITCODE -ne 0){throw '天气缓存验证失败'}
        & $sdk run --project "$PSScriptRoot/tests/RunnerVerification/RunnerVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- (Join-Path $sandbox "runner-$InputMode")
        if($LASTEXITCODE -ne 0){throw 'Runner故障恢复或提示词验证失败'}
        & $sdk run --project "$PSScriptRoot/tests/AnimationVerification/AnimationVerification.csproj" -c Release -- "$PSScriptRoot/VPet-Simulator.Windows/assets/fish"
        if($LASTEXITCODE -ne 0){throw '动画规则验证失败'}
        & $sdk run --project "$PSScriptRoot/tests/ForegroundVerification/ForegroundVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode"
        if($LASTEXITCODE -ne 0){throw '前台窗口验证失败'}
        & $sdk run --project "$PSScriptRoot/tests/AnimationUiVerification/AnimationUiVerification.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -- $sandbox
        if($LASTEXITCODE -ne 0){throw '动画与设置实窗验证失败'}
    }
} finally {Pop-Location}
