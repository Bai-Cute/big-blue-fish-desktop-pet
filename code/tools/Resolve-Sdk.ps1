param([string]$SdkPath)
$ErrorActionPreference='Stop'
$candidates=@()
if($SdkPath){$candidates+=$SdkPath}
if($env:BLUE_WHALE_DOTNET){$candidates+=$env:BLUE_WHALE_DOTNET}
$command=Get-Command dotnet -ErrorAction SilentlyContinue
if($command){$candidates+=$command.Source}
$candidates+=Join-Path $PSScriptRoot '../../../../work/dotnet-sdk/dotnet.exe'
foreach($candidate in $candidates){
    if(!(Test-Path -LiteralPath $candidate)){continue}
    $version=& $candidate --version 2>$null
    if($LASTEXITCODE -eq 0 -and $version -match '^10\.'){
        return (Resolve-Path -LiteralPath $candidate).Path
    }
}
throw '需要 .NET 10 SDK。安装后重试，或用 -SdkPath 指定 dotnet.exe。'
