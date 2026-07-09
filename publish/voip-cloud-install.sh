#!/bin/bash
# Smart OePNV VoIP Cloud – einmalige Server-Einrichtung (Ubuntu 24.04)
# Nutzung: bash voip-cloud-install.sh [HOSTNAME]

set -euo pipefail

PUBLIC_IP="${PUBLIC_IP:-82.165.167.157}"
HOSTNAME="${1:-82-165-167-157.sslip.io}"
TURN_USER="${TURN_USER:-smartoepnv}"
TURN_PASS="${TURN_PASS:-$(openssl rand -base64 18 | tr -d '/+=' | head -c 20)}"
INSTALL_DIR="/opt/voip-cloud"

echo "=== Smart OePNV VoIP Cloud Setup ==="
echo "IP:       $PUBLIC_IP"
echo "Hostname: $HOSTNAME"
echo "TURN:     $TURN_USER / $TURN_PASS"
echo

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq nginx coturn certbot python3-certbot-nginx ufw curl tar

ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 8787/tcp
ufw allow 3478/tcp
ufw allow 3478/udp
ufw allow 49152:49252/udp
ufw --force enable

mkdir -p "$INSTALL_DIR"
if [ -f /tmp/voip-cloud.tar.gz ]; then
  tar -xzf /tmp/voip-cloud.tar.gz -C "$INSTALL_DIR"
  chmod +x "$INSTALL_DIR/VoipCloudServer" 2>/dev/null || true
fi

cat > /etc/systemd/system/voip-cloud.service <<EOF
[Unit]
Description=SmartOepnv VoIP Cloud Signaling
After=network.target

[Service]
WorkingDirectory=$INSTALL_DIR
Environment=VOIP_LISTEN=127.0.0.1
Environment=VOIP_PORT=8787
ExecStart=$INSTALL_DIR/VoipCloudServer
Restart=always
RestartSec=3
User=www-data

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/nginx/sites-available/voip <<EOF
server {
    listen 80;
    server_name $HOSTNAME;

    location /voip/ws {
        proxy_pass http://127.0.0.1:8787;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_read_timeout 86400;
    }
}
EOF

ln -sf /etc/nginx/sites-available/voip /etc/nginx/sites-enabled/voip
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl daemon-reload
systemctl enable voip-cloud
systemctl restart voip-cloud
systemctl reload nginx

cat > /etc/turnserver.conf <<EOF
listening-port=3478
fingerprint
lt-cred-mech
realm=$HOSTNAME
user=$TURN_USER:$TURN_PASS
min-port=49152
max-port=49252
external-ip=$PUBLIC_IP
EOF

sed -i 's/^#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn 2>/dev/null || true
systemctl enable coturn
systemctl restart coturn

if certbot --nginx -d "$HOSTNAME" --non-interactive --agree-tos -m "admin@${HOSTNAME}" --redirect 2>/dev/null; then
  TLS_OK=1
else
  echo "Hinweis: Let's Encrypt fehlgeschlagen – DNS fuer $HOSTNAME pruefen"
  TLS_OK=0
fi

# certbot ueberschreibt oft die nginx-Config – WebSocket-Proxy fuer HTTPS erneut setzen
if [ -f "/etc/letsencrypt/live/${HOSTNAME}/fullchain.pem" ]; then
  bash /tmp/voip-nginx-fix.sh "$HOSTNAME" 2>/dev/null || true
fi

echo
echo "========== FERTIG =========="
echo "Signaling (wss): wss://$HOSTNAME/voip/ws"
if [ "$TLS_OK" != "1" ]; then
  echo "Signaling (ws, Fallback): ws://$PUBLIC_IP:8787/voip/ws"
fi
echo "TURN-Host:     $HOSTNAME"
echo "TURN-Port:     3478"
echo "TURN-User:     $TURN_USER"
echo "TURN-Passwort: $TURN_PASS"
echo
echo "Leitstelle: Smart OePNV Funk -> TURN-Passwort oben eintragen"
echo "============================"
