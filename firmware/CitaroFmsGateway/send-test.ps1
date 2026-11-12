# Schickt Testdaten an den Citaro-FMS-ESP32 über einen USB-RS485-Adapter.
# A des Adapters an Klemme A, B an B. Der ESP32 muss die Fassung mit RS485-Empfang haben.
# Werte verfallen nach 5 Sekunden, deshalb wiederholt das Skript die Zeile.
param(
    [string]$Port,
    [string]$Text = "speed=50 tank=70 rpm=900 t1_open=1",
    [int]$Baud = 9600,
    [int]$EveryMs = 2000
)

$ErrorActionPreference = "Stop"

function Get-AdapterPort {
    $devices = @(Get-PnpDevice -PresentOnly -Class Ports | Where-Object {
        $_.FriendlyName -match "\((COM\d+)\)" -and $_.InstanceId -notmatch "VID_303A&PID_1001"
    })
    if ($devices.Count -eq 0) {
        throw "Kein USB-RS485-Adapter. Den Stick einstecken, nicht den USB-Anschluss des ESP32."
    }
    if ($devices.Count -gt 1) {
        $list = ($devices | ForEach-Object { $_.FriendlyName }) -join ", "
        throw "Mehrere serielle Anschlüsse: $list. Einen angeben, zum Beispiel: .\send-test.ps1 -Port COM5"
    }
    if ($devices[0].FriendlyName -match "\((COM\d+)\)") {
        return $Matches[1]
    }
    throw "COM-Anschluss des Adapters nicht lesbar."
}

if (-not $Port) {
    $Port = Get-AdapterPort
}

$serial = New-Object System.IO.Ports.SerialPort $Port, $Baud, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
$serial.NewLine = "`n"
$serial.WriteTimeout = 1000
$serial.Open()
Write-Host "Sende an $Port, $Baud 8N1. Strg+C beendet. Zeile: $Text"
try {
    while ($true) {
        $serial.WriteLine($Text)
        Write-Host "$(Get-Date -Format HH:mm:ss) $Text"
        Start-Sleep -Milliseconds $EveryMs
    }
}
finally {
    if ($serial.IsOpen) {
        $serial.Close()
    }
}
