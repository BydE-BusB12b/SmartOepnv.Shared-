#!/bin/bash
# coturn für Smart ÖPNV Funk – Relay-IP und Firewall-Ports
set -euo pipefail

PUBLIC_IP="${PUBLIC_IP:-82.165.167.157}"
HOSTNAME="${HOSTNAME:-82-165-167-157.sslip.io}"
TURN_USER="${TURN_USER:-smartoepnv}"

if [ -f /etc/turnserver.conf ]; then
  TURN_PASS="$(grep -m1 "^user=${TURN_USER}:" /etc/turnserver.conf | cut -d: -f2- || true)"
fi
TURN_PASS="${TURN_PASS:-CHANGE_ME}"

cat > /etc/turnserver.conf <<EOF
listening-ip=0.0.0.0
relay-ip=${PUBLIC_IP}
listening-port=3478
fingerprint
lt-cred-mech
realm=${HOSTNAME}
user=${TURN_USER}:${TURN_PASS}
min-port=49152
max-port=49252
external-ip=${PUBLIC_IP}
no-cli
EOF

systemctl restart coturn
ufw allow 3478/tcp
ufw allow 3478/udp
ufw allow 49152:49252/udp

echo "coturn neu gestartet – Relay ${PUBLIC_IP}, Ports 3478 + 49152-49252/udp"
