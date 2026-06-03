#!/bin/bash
set -euo pipefail

LOG_FILE="/tmp/hsed_usb_update.log"
exec > >(tee -a "$LOG_FILE") 2>&1

progress() {
  local p="$1"
  if (( p < 0 )); then p=0; fi
  if (( p > 100 )); then p=100; fi
  echo "PROGRESS: ${p}"
}

echo "==============================="
echo "HSED USB Update gestartet: $(date)"
echo "==============================="

ARCHIVE_NAME="LinuxArm.rar"
TMP_DIR="/tmp/hsed_usb_update"
MOUNT_DIR="/tmp/hsed_usb"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OTA_SCRIPT="$SCRIPT_DIR/update_hsed.sh"

mkdir -p "$TMP_DIR" "$MOUNT_DIR"

progress 5

if [[ ! -f "$OTA_SCRIPT" ]]; then
  echo "FEHLER: update_hsed.sh nicht gefunden: $OTA_SCRIPT"
  exit 1
fi

if ! command -v lsblk >/dev/null 2>&1; then
  echo "FEHLER: lsblk fehlt"
  exit 1
fi

progress 10

# Genau 1 USB DISK
mapfile -t USB_DISKS < <(lsblk -rpno NAME,TYPE,TRAN | awk '$2=="disk" && $3=="usb" {print $1}')

if [[ ${#USB_DISKS[@]} -eq 0 ]]; then
  echo "FEHLER: Kein USB Stick gefunden"
  exit 2
fi

if [[ ${#USB_DISKS[@]} -gt 1 ]]; then
  echo "FEHLER: Es darf nur ein USB Stick eingesteckt sein"
  printf '%s\n' "${USB_DISKS[@]}"
  exit 3
fi

USB_DISK="${USB_DISKS[0]}"
echo "USB Disk: $USB_DISK"

progress 20

# Erste Partition der Disk finden
mapfile -t USB_PARTS < <(lsblk -rpno NAME,TYPE | awk -v d="$USB_DISK" '$2=="part" && index($1, d)==1 {print $1}')

if [[ ${#USB_PARTS[@]} -eq 0 ]]; then
  echo "FEHLER: Keine Partition auf dem USB Stick gefunden"
  exit 4
fi

USB_PART="${USB_PARTS[0]}"
echo "USB Partition: $USB_PART"

progress 30

# Mountpoint pr�fen
MOUNTED_PATH="$(lsblk -no MOUNTPOINT "$USB_PART" | head -n 1 | tr -d '[:space:]' || true)"

if [[ -z "$MOUNTED_PATH" ]]; then
  if command -v udisksctl >/dev/null 2>&1; then
    echo "Mount via udisksctl..."
    OUT="$(udisksctl mount -b "$USB_PART" 2>&1 || true)"
    echo "$OUT"
    MOUNTED_PATH="$(echo "$OUT" | grep -oE 'at /[^ ]+' | awk '{print $2}' | tail -n 1 || true)"
  else
    echo "FEHLER: udisksctl fehlt. Bitte udisks2 installieren oder Automount aktivieren."
    exit 5
  fi
fi

if [[ -z "$MOUNTED_PATH" ]]; then
  MOUNTED_PATH="$(lsblk -no MOUNTPOINT "$USB_PART" | head -n 1 | tr -d '[:space:]' || true)"
fi

if [[ -z "$MOUNTED_PATH" ]]; then
  echo "FEHLER: Konnte USB Stick nicht mounten"
  exit 6
fi

echo "USB gemountet unter: $MOUNTED_PATH"

progress 45

ARCHIVE_PATH="$MOUNTED_PATH/$ARCHIVE_NAME"
if [[ ! -f "$ARCHIVE_PATH" ]]; then
  echo "FEHLER: Datei $ARCHIVE_NAME nicht gefunden im Hauptverzeichnis"
  echo "Erwartet: $ARCHIVE_PATH"
  ls -la "$MOUNTED_PATH" || true
  exit 7
fi

progress 60

LOCAL_ARCHIVE="$TMP_DIR/$ARCHIVE_NAME"
echo "Kopiere Archiv lokal..."
cp -f "$ARCHIVE_PATH" "$LOCAL_ARCHIVE"

progress 75

echo "Starte Installation �ber update_hsed.sh --source"
chmod +x "$OTA_SCRIPT" || true

/bin/bash "$OTA_SCRIPT" --source "$LOCAL_ARCHIVE"
