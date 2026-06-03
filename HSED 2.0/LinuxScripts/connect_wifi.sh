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

IFACE="$(detect_iface "${IFACE:-}" || true)"
CTRL="/run/wpa_supplicant"
SSID="${1:-}"
PSK="${2:-}"

j() { printf "%s" "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\r//g'; }

have_nmcli() {
  command -v nmcli >/dev/null 2>&1 && nmcli general status >/dev/null 2>&1
}

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

if [ -z "$IFACE" ]; then
  emit false "Fehler beim Verbinden" "iface_not_found" "$SSID" ""
  exit 2
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

if have_nmcli; then
  nmcli radio wifi on >/dev/null 2>&1 || true
  nmcli device set "$IFACE" managed yes >/dev/null 2>&1 || true

  if [ -n "$PSK" ]; then
    NM_OUT="$(nmcli --wait 30 device wifi connect "$SSID" password "$PSK" ifname "$IFACE" 2>&1 || true)"
  else
    NM_OUT="$(nmcli --wait 30 device wifi connect "$SSID" ifname "$IFACE" 2>&1 || true)"
  fi

  STATE="$(nmcli -t -g GENERAL.STATE device show "$IFACE" 2>/dev/null | head -n1 || true)"
  IP="$(nmcli -t -g IP4.ADDRESS device show "$IFACE" 2>/dev/null | head -n1 || true)"
  IP="${IP%%/*}"

  case "$STATE" in
    100*|100\ *)
      if [ -n "$IP" ]; then
        emit true "Verbunden" "ok" "$SSID" "$IP"
        exit 0
      fi
      emit false "Fehler beim Verbinden" "no_ip_after_completed" "$SSID" ""
      exit 2
      ;;
  esac

  if echo "$NM_OUT" | grep -qi "Secrets were required"; then
    emit false "Fehler beim Verbinden" "invalid_psk" "$SSID" ""
    exit 2
  fi

  emit false "Fehler beim Verbinden" "nmcli_connect_failed" "$SSID" ""
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
