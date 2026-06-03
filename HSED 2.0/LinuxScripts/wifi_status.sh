#!/bin/sh
set -eu

detect_iface() {
  if [ -n "${1:-}" ] && ip link show "$1" >/dev/null 2>&1; then
    printf "%s\n" "$1"
    return 0
  fi

  if command -v iw >/dev/null 2>&1; then
    IFACE_CANDIDATE="$(iw dev 2>/dev/null | awk '$1=="Interface"{print $2; exit}')"
    if [ -n "$IFACE_CANDIDATE" ]; then
      printf "%s\n" "$IFACE_CANDIDATE"
      return 0
    fi
  fi

  IFACE_CANDIDATE="$(ip -o link show 2>/dev/null | awk -F': ' '{print $2}' | grep -E '^(wl|wlan)' | head -n1 || true)"
  if [ -n "$IFACE_CANDIDATE" ]; then
    printf "%s\n" "$IFACE_CANDIDATE"
    return 0
  fi

  return 1
}

IFACE="$(detect_iface "${1:-}" || true)"

if [ -z "$IFACE" ]; then
  echo "{\"state\":\"unknown\",\"ssid\":\"\",\"ip\":\"\"}"
  exit 0
fi

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
