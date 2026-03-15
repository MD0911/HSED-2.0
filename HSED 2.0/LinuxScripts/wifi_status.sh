#!/bin/sh
set -eu

IFACE="${1:-wlan0}"

STATE="$(wpa_cli -i "$IFACE" status 2>/dev/null | awk -F= "/^wpa_state=/{print \$2}" | head -n1)"
SSID="$(wpa_cli -i "$IFACE" status 2>/dev/null | awk -F= "/^ssid=/{print \$2}" | head -n1)"
IP="$(ip -4 addr show dev "$IFACE" | awk "/inet /{print \$2}" | head -n1)"

[ -z "$STATE" ] && STATE="DISCONNECTED"
[ -z "$SSID" ] && SSID=""

echo "{\"state\":\"$STATE\",\"ssid\":\"$SSID\",\"ip\":\"$IP\"}"
