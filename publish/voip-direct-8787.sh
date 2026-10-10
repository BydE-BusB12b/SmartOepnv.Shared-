#!/bin/bash
# VoipCloudServer oeffentlich auf Port 8787 (ohne nginx)
set -euo pipefail

sed -i 's/VOIP_LISTEN=127.0.0.1/VOIP_LISTEN=0.0.0.0/' /etc/systemd/system/voip-cloud.service 2>/dev/null || true
if ! grep -q VOIP_LISTEN /etc/systemd/system/voip-cloud.service; then
  sed -i '/Environment=VOIP_PORT/a Environment=VOIP_LISTEN=0.0.0.0' /etc/systemd/system/voip-cloud.service
fi

systemctl daemon-reload
systemctl restart voip-cloud
sleep 2

echo "=== Test ==="
curl -sI http://127.0.0.1:8787/voip/ws | head -1
curl -sI http://82.165.167.157:8787/voip/ws | head -1
