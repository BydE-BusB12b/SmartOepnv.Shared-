# Smart OePNV – nginx WebSocket-Proxy reparieren (einmal ausfuehren)
# Passwort: IONOS "Passwort anzeigen" (root) – NICHT das TURN-Passwort!

$ErrorActionPreference = "Stop"
$server = "82.165.167.157"
$hostName = "82-165-167-157.sslip.io"
$localScript = Join-Path $PSScriptRoot "voip-nginx-fix.sh"

if (-not (Test-Path $localScript)) {
    Write-Host "Fehlt: $localScript" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Schritt 1/2: Fix-Skript hochladen ===" -ForegroundColor Cyan
Write-Host "Passwort = IONOS Root-Passwort (Passwort anzeigen im Cloud Panel)" -ForegroundColor Yellow
scp $localScript "root@${server}:/tmp/voip-nginx-fix.sh"

Write-Host ""
Write-Host "=== Schritt 2/2: Auf dem Server ausfuehren ===" -ForegroundColor Cyan
Write-Host "Gleiches IONOS Root-Passwort nochmal" -ForegroundColor Yellow
ssh "root@$server" @"
sed -i 's/\r$//' /tmp/voip-nginx-fix.sh
bash /tmp/voip-nginx-fix.sh
echo '--- Test ---'
curl -I https://${hostName}/voip/ws
"@

Write-Host ""
Write-Host "Fertig. Oben muss stehen: HTTP/1.1 200 OK" -ForegroundColor Green
Write-Host "TURN-Passwort brauchst du nur in der Leitstelle (VoIP-Einstellungen)." -ForegroundColor Gray
