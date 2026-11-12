# Spielt das gespeicherte Citaro-FMS-Abbild auf ein per USB angeschlossenes
# Waveshare ESP32-S3-RS485-CAN oder ESP32-S3-RS485-CAN-U.
# Ohne -Port wird der eine angeschlossene Espressif-USB-Anschluss genommen.
param(
    [string]$Port
)

$ErrorActionPreference = "Stop"
$image = Join-Path $PSScriptRoot "image"

function Get-EspPort {
    $devices = @(Get-PnpDevice -PresentOnly -Class Ports | Where-Object {
        $_.InstanceId -match "VID_303A&PID_1001" -and $_.FriendlyName -match "\((COM\d+)\)"
    })
    if ($devices.Count -eq 0) {
        throw "Kein ESP32 am USB. Das Board einstecken und das Skript erneut starten."
    }
    if ($devices.Count -gt 1) {
        $list = ($devices | ForEach-Object { $_.FriendlyName }) -join ", "
        throw "Mehrere ESP32: $list. Einen Anschluss angeben, zum Beispiel: .\flash.ps1 -Port COM7"
    }
    if ($devices[0].FriendlyName -match "\((COM\d+)\)") {
        return $Matches[1]
    }
    throw "COM-Anschluss am ESP32 nicht lesbar."
}

foreach ($name in @("bootloader.bin", "partitions.bin", "boot_app0.bin", "firmware.bin")) {
    $path = Join-Path $image $name
    if (-not (Test-Path $path)) {
        throw "Abbild fehlt: $path"
    }
}

if (-not $Port) {
    $Port = Get-EspPort
}

Write-Host "Bespielen $Port mit dem Citaro-FMS-Gateway"
& py -3 -m esptool --chip esp32s3 --port $Port --baud 921600 write-flash --flash-mode dio --flash-freq 80m --flash-size 16MB 0x0 (Join-Path $image "bootloader.bin") 0x8000 (Join-Path $image "partitions.bin") 0xe000 (Join-Path $image "boot_app0.bin") 0x10000 (Join-Path $image "firmware.bin")
if ($LASTEXITCODE -ne 0) {
    throw "Bespielen von $Port fehlgeschlagen."
}
Write-Host "Fertig. $Port meldet sich als Citaro_FMS_Gateway."
