#!/bin/sh
# Doppelklick auf das Desktop-Symbol: läuft die Anzeige, wird sie beendet.
pids=$(ps -eo pid,args | awk '/python3/ && /gorba_innen.py/ && /--anzeige/ && !/awk/ {print $1}')
if [ -n "$pids" ]; then
  kill $pids
  pkill -x avahi-publish-service
  exit 0
fi
export DISPLAY=:0
export XAUTHORITY=/home/kiara/.Xauthority
exec python3 -u /home/kiara/smart-oepnv-ip/gorba_innen.py --anzeige
