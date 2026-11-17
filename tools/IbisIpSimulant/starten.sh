#!/bin/bash
cd "$(dirname "$0")"
exec python3 -u ibis_ip_simulant.py --port "${1:-8081}"
