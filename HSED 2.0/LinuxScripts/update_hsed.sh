#!/bin/bash
set -euo pipefail

LOG_FILE="/tmp/hsed_update.log"
exec > >(tee -a "$LOG_FILE") 2>&1

progress() {
  local p="$1"
  if (( p < 0 )); then p=0; fi
  if (( p > 100 )); then p=100; fi
  echo "PROGRESS: ${p}"
}

echo "==============================="
echo "HSED Update gestartet: $(date)"
echo "User: $(whoami)"
echo "PWD: $(pwd)"
echo "==============================="

# --------------------
# KONFIG
# --------------------
REPO="MD0911/HSED-2.0"
INSTALL_BASE="$HOME/HSED"
CURRENT_DIR="$INSTALL_BASE/LinuxArm"
STAGING_DIR="$INSTALL_BASE/staging"
TMP_DIR="/tmp/hsed_update"
ARCHIVE_NAME="LinuxArm.rar"

CONFIG_FILE="$CURRENT_DIR/config.json"
CONFIG_BACKUP="$TMP_DIR/config.json.backup"

VERSION=""
SOURCE=""
NO_REBOOT=false

# --------------------
# PARAMETER
# --------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      VERSION="$2"
      shift 2
      ;;
    --source)
      SOURCE="$2"
      shift 2
      ;;
    --no-reboot)
      NO_REBOOT=true
      shift
      ;;
    *)
      echo "Unbekannter Parameter: $1"
      exit 1
      ;;
  esac
done

# --------------------
# CHECKS
# --------------------
if [[ -z "$VERSION" && -z "$SOURCE" ]]; then
  echo "FEHLER: --version oder --source erforderlich"
  exit 1
fi

if ! command -v unrar >/dev/null 2>&1; then
  echo "FEHLER: unrar fehlt"
  exit 1
fi

# --------------------
# PREP
# --------------------
echo "Bereite Update vor..."
progress 5
rm -rf "$TMP_DIR"
mkdir -p "$TMP_DIR" "$STAGING_DIR"
progress 10

# --------------------
# DOWNLOAD
# --------------------
if [[ -n "$SOURCE" ]]; then
  echo "Offline Update von $SOURCE"
  cp "$SOURCE" "$TMP_DIR/$ARCHIVE_NAME"
else
  DOWNLOAD_URL="https://github.com/$REPO/releases/download/$VERSION/$ARCHIVE_NAME"
  echo "Download: $DOWNLOAD_URL"
  progress 15

  # -L: redirects folgen
  wget -L -O "$TMP_DIR/$ARCHIVE_NAME" "$DOWNLOAD_URL"
fi

progress 40

# --------------------
# VALIDIEREN (WICHTIG)
# --------------------
if ! unrar t "$TMP_DIR/$ARCHIVE_NAME" >/dev/null 2>&1; then
  echo "FEHLER: Download ist kein g ltiges RAR Archiv."
  echo "Quelle: ${SOURCE:-$DOWNLOAD_URL}"
  echo "Dateityp: $(file -b "$TMP_DIR/$ARCHIVE_NAME" || true)"
  echo "Erste Zeilen (falls HTML/Fehlerseite):"
  head -n 10 "$TMP_DIR/$ARCHIVE_NAME" || true
  exit 1
fi

progress 50

# --------------------
# INSTALL
# --------------------
echo "Entpacke..."
rm -rf "$STAGING_DIR"/*
unrar x "$TMP_DIR/$ARCHIVE_NAME" "$STAGING_DIR"
progress 70

# Config sichern
if [[ -f "$CONFIG_FILE" ]]; then
  echo "Sichere bestehende config.json..."
  cp "$CONFIG_FILE" "$CONFIG_BACKUP"
else
  echo "Keine bestehende config.json gefunden, nichts zu sichern."
fi

progress 75

echo "Installiere..."
rm -rf "$CURRENT_DIR"
mkdir -p "$INSTALL_BASE"
mv "$STAGING_DIR/LinuxArm" "$CURRENT_DIR"
progress 90

# Config wiederherstellen
if [[ -f "$CONFIG_BACKUP" ]]; then
  echo "Stelle config.json wieder her..."
  cp "$CONFIG_BACKUP" "$CURRENT_DIR/config.json"
fi

progress 95

# --------------------
# CLEANUP
# --------------------
rm -rf "$TMP_DIR" "$STAGING_DIR"
echo "Update erfolgreich installiert."
progress 100

# --------------------
# REBOOT
# --------------------
if [[ "$NO_REBOOT" == false ]]; then
  echo "Starte Reboot..."
  /sbin/reboot
else
  echo "Reboot  bersprungen (--no-reboot)"
fi
