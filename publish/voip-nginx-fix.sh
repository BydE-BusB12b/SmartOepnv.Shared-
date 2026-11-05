#!/bin/bash
# nginx WebSocket-Proxy fuer Smart OePNV Funk reparieren (nach certbot)
set -euo pipefail

HOSTNAME="${1:-82-165-167-157.sslip.io}"
CERT="/etc/letsencrypt/live/${HOSTNAME}/fullchain.pem"
KEY="/etc/letsencrypt/live/${HOSTNAME}/privkey.pem"

if [ ! -f "$CERT" ]; then
  echo "Zertifikat fehlt: $CERT"
  echo "Zuerst: certbot --nginx -d $HOSTNAME"
  exit 1
fi

cat > /etc/nginx/sites-available/voip <<EOF
server {
    listen 80;
    server_name ${HOSTNAME};

    location /voip/ws {
        proxy_pass http://127.0.0.1:8787;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_read_timeout 86400;
    }
}

server {
    listen 443 ssl;
    server_name ${HOSTNAME};

    ssl_certificate ${CERT};
    ssl_certificate_key ${KEY};
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

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
rm -f /etc/nginx/conf.d/default.conf
nginx -t
systemctl reload nginx
systemctl restart voip-cloud || true

echo "OK: https://${HOSTNAME}/voip/ws sollte jetzt erreichbar sein."
