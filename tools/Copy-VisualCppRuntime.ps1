param([Parameter(Mandatory=$true)][string]$Destination,[string]$SourceDirectory)
$ErrorActionPreference='Stop'
if(!$SourceDirectory){
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if(Test-Path -LiteralPath $vswhere){
        $installation=& $vswhere -latest -products '*' -property installationPath
        if($installation){
            $redist=Join-Path $installation 'VC/Redist/MSVC'
            $version=Get-ChildItem -LiteralPath $redist -Directory | Where-Object {$_.Name -match '^\d+\.\d+\.\d+$'} | Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1
            if($version){ $SourceDirectory=(Get-ChildItem -LiteralPath (Join-Path $version.FullName 'x64') -Directory -Filter 'Microsoft.VC*.CRT' | Select-Object -First 1).FullName }
        }
    }
}
if(!$SourceDirectory){throw '请通过 -VisualCppDirectory 提供 Microsoft Visual C++ x64 可再发行 CRT 文件夹。'}
$names=@('concrt140.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll',
    'msvcp140_codecvt_ids.dll','vccorlib140.dll','vcruntime140.dll','vcruntime140_1.dll')
if(Test-Path -LiteralPath (Join-Path $SourceDirectory 'vcruntime140_threads.dll')){$names+='vcruntime140_threads.dll'}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$manifest=foreach($name in $names){
    $source=Join-Path $SourceDirectory $name
    if(!(Test-Path -LiteralPath $source)){throw "Visual C++ CRT 缺少 $name"}
    $signature=Get-AuthenticodeSignature -LiteralPath $source
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){
        throw "Visual C++ CRT 签名不符合要求：$name"
    }
    $bytes=[IO.File]::ReadAllBytes($source)
    $offset=[BitConverter]::ToInt32($bytes,60)
    if([BitConverter]::ToUInt16($bytes,$offset+4) -ne 0x8664){throw "Visual C++ CRT 不是 x64：$name"}
    Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $name)
    [ordered]@{file=$name;version=(Get-Item -LiteralPath $source).VersionInfo.FileVersion;
        sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Destination 'visual-cpp-runtime.json') -Encoding UTF8
Write-Host "已配置应用内 Visual C++ x64 CRT：$SourceDirectory"
