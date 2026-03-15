#!/bin/sh
set -eu

IFACE="${IFACE:-wlan0}"
CTRL="/run/wpa_supplicant"
SSID="${1:-}"
PSK="${2:-}"

j() { printf "%s" "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\r//g'; }

emit() {
  ok="$1"           # true/false
  msg="$2"          # "Verbunden" oder "Fehler beim Verbinden"
  reason="$3"       # z.B. timeout, no_ip_after_completed ...
  ssid="$4"
  ip="$5"
  echo "{
\"ok\":$ok,
\"msg\":\"$(j "$msg")\",
\"reason\":\"$(j "$reason")\",
\"ssid\":\"$(j "$ssid")\",
\"ip\":\"$(j "$ip")\"
}"
}

if [ -z "$SSID" ]; then
  emit false "Fehler beim Verbinden" "missing_ssid" "" ""
  exit 1
fi

if [ -n "$PSK" ]; then
  LEN="$(printf "%s" "$PSK" | wc -c | tr -d ' ')"
  if [ "$LEN" -lt 8 ] || [ "$LEN" -gt 63 ]; then
    emit false "Fehler beim Verbinden" "invalid_psk_length" "$SSID" ""
    exit 1
  fi
fi

if ! ip link show "$IFACE" >/dev/null 2>&1; then
  emit false "Fehler beim Verbinden" "iface_not_found" "$SSID" ""
  exit 2
fi

PING="$(wpa_cli -i "$IFACE" -p "$CTRL" ping 2>/dev/null || true)"
if ! echo "$PING" | grep -q PONG; then
  emit false "Fehler beim Verbinden" "wpa_cli_no_pong" "$SSID" ""
  exit 2
fi

# Alte IP weg
ip -4 addr flush dev "$IFACE" 2>/dev/null || true

# Verbindung trennen
wpa_cli -i "$IFACE" -p "$CTRL" disconnect >/dev/null 2>&1 || true

# Neues Network anlegen
NID="$(wpa_cli -i "$IFACE" -p "$CTRL" add_network 2>/dev/null || true)"
echo "$NID" | grep -Eq '^[0-9]+$' || {
  emit false "Fehler beim Verbinden" "add_network_failed" "$SSID" ""
  exit 2
}

wpa_cli -i "$IFACE" -p "$CTRL" set_network "$NID" ssid "\"$SSID\"" >/dev/null 2>&1 || true
if [ -n "$PSK" ]; then
  wpa_cli -i "$IFACE" -p "$CTRL" set_network "$NID" psk "\"$PSK\"" >/dev/null 2>&1 || true
else
  wpa_cli -i "$IFACE" -p "$CTRL" set_network "$NID" key_mgmt NONE >/dev/null 2>&1 || true
fi

wpa_cli -i "$IFACE" -p "$CTRL" enable_network "$NID" >/dev/null 2>&1 || true
wpa_cli -i "$IFACE" -p "$CTRL" select_network "$NID" >/dev/null 2>&1 || true
wpa_cli -i "$IFACE" -p "$CTRL" save_config >/dev/null 2>&1 || true

# Auth abwarten
STATE=""
CURSSID=""
t=0
while [ $t -lt 35 ]; do
  SR="$(wpa_cli -i "$IFACE" -p "$CTRL" status 2>/dev/null || true)"
  STATE="$(printf "%s" "$SR" | awk -F= '/^wpa_state=/{print $2}' | head -n1 || true)"
  CURSSID="$(printf "%s" "$SR" | awk -F= '/^ssid=/{print $2}' | head -n1 || true)"
  [ "$STATE" = "COMPLETED" ] && [ "$CURSSID" = "$SSID" ] && break
  sleep 1
  t=$((t+1))
done

if [ "$STATE" != "COMPLETED" ] || [ "$CURSSID" != "$SSID" ]; then
  emit false "Fehler beim Verbinden" "timeout_or_wrong_ssid" "$CURSSID" ""
  exit 2
fi

# DHCP
if command -v dhcpcd >/dev/null 2>&1; then
  dhcpcd -n "$IFACE" >/dev/null 2>&1 || true
elif command -v udhcpc >/dev/null 2>&1; then
  udhcpc -i "$IFACE" -q -n >/dev/null 2>&1 || true
fi

IP="$(ip -4 addr show dev "$IFACE" 2>/dev/null | awk '/inet /{print $2}' | head -n1 || true)"
if [ -z "$IP" ]; then
  emit false "Fehler beim Verbinden" "no_ip_after_completed" "$SSID" ""
  exit 2
fi

emit true "Verbunden" "ok" "$SSID" "$IP"
exit 0
