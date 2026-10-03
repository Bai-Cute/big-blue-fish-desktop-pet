param([string]$Version)
$ErrorActionPreference = 'Stop'
if (!$Version) {
    [xml]$projectVersion = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Version.props') -Raw
    $Version = [string]$projectVersion.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') {
    throw '版本号应由三个数字段组成，可附加预发布名称。'
}
return $Version
