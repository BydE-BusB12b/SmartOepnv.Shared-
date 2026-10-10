#!/bin/bash
# nginx komplett neu setzen – nur Smart OePNV Funk
set -euo pipefail

HOSTNAME="82-165-167-157.sslip.io"
CERT="/etc/letsencrypt/live/${HOSTNAME}/fullchain.pem"
KEY="/etc/letsencrypt/live/${HOSTNAME}/privkey.pem"

systemctl stop nginx 2>/dev/null || true
rm -f /etc/nginx/sites-enabled/*
rm -f /etc/nginx/conf.d/*.conf

cat > /etc/nginx/conf.d/voip.conf <<NGINX
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name ${HOSTNAME} _;

    location = /voip/ws {
        proxy_pass http://127.0.0.1:8787;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_read_timeout 86400;
    }
}

server {
    listen 443 ssl default_server;
    listen [::]:443 ssl default_server;
    server_name ${HOSTNAME} _;

    ssl_certificate ${CERT};
    ssl_certificate_key ${KEY};
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    location = /voip/ws {
        proxy_pass http://127.0.0.1:8787;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_read_timeout 86400;
    }
}
NGINX

nginx -t
systemctl restart voip-cloud
sleep 2
systemctl start nginx

if ! curl -sf -o /dev/null -H "Host: ${HOSTNAME}" http://127.0.0.1:8787/voip/ws; then
  echo "FEHLER: VoipCloudServer antwortet nicht auf 127.0.0.1:8787"
  systemctl status voip-cloud --no-pager || true
  journalctl -u voip-cloud -n 20 --no-pager || true
  exit 1
fi

echo "=== Test ==="
curl -sI -H "Host: ${HOSTNAME}" http://127.0.0.1/voip/ws | head -1
curl -sIk -H "Host: ${HOSTNAME}" https://127.0.0.1/voip/ws | head -1
curl -sI "https://${HOSTNAME}/voip/ws" | head -1
