param([string]$SdkPath,[switch]$Locked)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $sdk=& "$PSScriptRoot/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
    $project=Join-Path $PSScriptRoot 'VPet-Simulator.Windows/VPet-Simulator.Windows.csproj'
    $argsBuild=@('build',$project,'-c','Release','-p:Platform=x64','--nologo')
    if($Locked){$argsBuild+='-p:RestoreLockedMode=true'}
    & $sdk @argsBuild
    if($LASTEXITCODE -ne 0){throw '编译失败'}
} finally {Pop-Location}
