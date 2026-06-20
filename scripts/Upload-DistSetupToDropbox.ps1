# Wrapper: lädt Setup.exe aus dist\ nach Dropbox (nutzt dieselbe C#-API wie der Planer).
param(
    [Parameter(Mandatory = $true)]
    [string]$SetupExePath,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Planer', 'Leitstelle')]
    [string]$Product,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolProject = Join-Path $scriptDir '..\tools\UploadInstallerTool\UploadInstallerTool.csproj'
if (-not (Test-Path $toolProject)) {
    throw "Upload-Tool nicht gefunden: $toolProject"
}

Write-Host "3/3 Dropbox: Upload über Planer-Dropbox-API..." -ForegroundColor Yellow
dotnet run --project $toolProject -c Release -- $SetupExePath $Product $Version
if ($LASTEXITCODE -ne 0) {
    throw "Dropbox-Upload fehlgeschlagen (Exit-Code $LASTEXITCODE)."
}

Write-Host "Dropbox-Upload abgeschlossen." -ForegroundColor Green
