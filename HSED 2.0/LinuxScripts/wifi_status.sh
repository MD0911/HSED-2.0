#!/bin/sh
set -eu

IFACE="${1:-wlan0}"

if command -v nmcli >/dev/null 2>&1 && nmcli general status >/dev/null 2>&1; then
  STATE_RAW="$(nmcli -t -g GENERAL.STATE device show "$IFACE" 2>/dev/null | head -n1 || true)"
  SSID="$(nmcli -t -g GENERAL.CONNECTION device show "$IFACE" 2>/dev/null | head -n1 || true)"
  IP="$(nmcli -t -g IP4.ADDRESS device show "$IFACE" 2>/dev/null | head -n1 || true)"

  case "$STATE_RAW" in
    100*|100\ *) STATE="COMPLETED" ;;
    30*|30\ *) STATE="DISCONNECTED" ;;
    20*|20\ *) STATE="UNAVAILABLE" ;;
    *) STATE="${STATE_RAW:-DISCONNECTED}" ;;
  esac

  IP="${IP%%/*}"
else
  STATE="$(wpa_cli -i "$IFACE" status 2>/dev/null | awk -F= "/^wpa_state=/{print \$2}" | head -n1)"
  SSID="$(wpa_cli -i "$IFACE" status 2>/dev/null | awk -F= "/^ssid=/{print \$2}" | head -n1)"
  IP="$(ip -4 addr show dev "$IFACE" | awk "/inet /{print \$2}" | head -n1)"
fi

[ -z "$STATE" ] && STATE="DISCONNECTED"
[ -z "$SSID" ] && SSID=""

echo "{\"state\":\"$STATE\",\"ssid\":\"$SSID\",\"ip\":\"$IP\"}"
