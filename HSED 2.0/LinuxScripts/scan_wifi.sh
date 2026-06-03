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
  echo "[]"
  exit 0
fi

OUT="$(iw dev "$IFACE" scan 2>/dev/null || true)"

if [ -z "$OUT" ]; then
  echo "[]"
  exit 0
fi

echo "$OUT" | awk '
BEGIN {
  print "["
  first=1
  ssid=""
  signal=""
  freq=""
  rsn=0
  wpa=0
}

function esc(s) {
  gsub(/\\/,"\\\\",s)
  gsub(/"/,"\\\"",s)
  gsub(/\t/,"\\t",s)
  gsub(/\r/,"\\r",s)
  gsub(/\n/,"\\n",s)
  return s
}

function flush() {
  if (ssid == "") return

  sec="open"
  if (rsn==1 || wpa==1) sec="secured"

  sig="null"
  if (signal != "") {
    sig = sprintf("%.0f", signal + 0)
  }

  fr="null"
  if (freq != "") {
    fr = sprintf("%d", freq + 0)
  }

  if (first==0) printf(",")
  first=0

  printf("{\"Ssid\":\"%s\",\"Security\":\"%s\",\"SignalDbm\":%s,\"FreqMhz\":%s}",
    esc(ssid), sec, sig, fr)

  ssid=""
  signal=""
  freq=""
  rsn=0
  wpa=0
}

$1=="BSS" { flush() }
$1=="freq:" { freq=$2 }
$1=="signal:" { signal=$2 }
$1=="SSID:" {
  $1=""
  sub(/^ /,"")
  ssid=$0
}
$1=="RSN:" { rsn=1 }
$1=="WPA:" { wpa=1 }

END {
  flush()
  print "\n]"
}
'
