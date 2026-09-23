# Compila DeepLocal (self-contained, win-x64) e crea i due installer:
#   installer\Output\DeepLocal_Setup_EN.exe
#   installer\Output\DeepLocal_Setup_ITA.exe
#   installer\Output\SHA256SUMS.txt
# Uso: powershell -ExecutionPolicy Bypass -File installer\build.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "publish\win-x64"
$output = Join-Path $PSScriptRoot "Output"

# Versione letta dal .csproj, così installer e programma coincidono
[xml]$proj = Get-Content (Join-Path $root "DeepLocal.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "DeepLocal $version"

# Inno Setup: nel PATH, oppure nei percorsi di installazione standard
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "Inno Setup 6 non trovato: winget install JRSoftware.InnoSetup" }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "DeepLocal.csproj") -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallito" }

New-Item -ItemType Directory -Force $output | Out-Null
foreach ($lang in "en", "it") {
    & $iscc "/DLang=$lang" "/DAppVersion=$version" (Join-Path $PSScriptRoot "DeepLocal.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup fallito ($lang)" }
}

$sums = Get-ChildItem $output -Filter "DeepLocal_Setup_*.exe" | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
$sums | Set-Content (Join-Path $output "SHA256SUMS.txt") -Encoding ascii
$sums
Get-ChildItem $output | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
