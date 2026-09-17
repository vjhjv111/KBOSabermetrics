#!/bin/sh
set -eu

if [ "$#" -ne 4 ]; then
  echo "usage: Remote-Incremental.sh TOOL LIVE_DB INBOX RUN_ID" >&2
  exit 2
fi

tool=$1
live=$2
inbox=$3
run_id=$4
work="${live}.${run_id}.working"
report="${work}.verify.json"

cleanup() {
  rm -f "$work" "${work}-wal" "${work}-shm" "$report"
  rm -rf "$inbox"
}
trap cleanup EXIT HUP INT TERM

if [ ! -x "$tool" ]; then
  echo "remote importer is missing or not executable: $tool" >&2
  exit 3
fi
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$(dirname "$tool")/.net"
mkdir -p "$DOTNET_BUNDLE_EXTRACT_BASE_DIR"
if [ ! -f "$live" ]; then
  echo "live database does not exist: $live" >&2
  exit 4
fi
if [ -s "${live}-wal" ]; then
  echo "live database has a non-empty WAL; refusing an inconsistent copy" >&2
  exit 5
fi

live_size=$(stat -c %s "$live")
available_kb=$(df -Pk "$live" | awk 'NR==2 {print $4}')
available_bytes=$((available_kb * 1024))
required_bytes=$((live_size + 268435456))
if [ "$available_bytes" -lt "$required_bytes" ]; then
  echo "not enough remote disk space for a safe staging copy: available=$available_bytes required=$required_bytes" >&2
  exit 6
fi

rm -f "$work" "${work}-wal" "${work}-shm" "$report"
cp --reflink=auto --sparse=always "$live" "$work"

import_output=$($tool --import "$inbox" "$work")
printf '%s\n' "$import_output"
imported=$(printf '%s\n' "$import_output" | sed -n 's/.*imported=\([0-9][0-9]*\).*/\1/p' | tail -n 1)
if [ -z "$imported" ]; then
  echo "remote importer did not report imported count" >&2
  exit 7
fi
if [ "$imported" -eq 0 ]; then
  echo "REMOTE_RESULT=NO_CHANGE imported=0"
  exit 0
fi

$tool --seal-web "$work"
$tool --verify "$work" "$report" --structural-only
new_size=$(stat -c %s "$work")
if [ "$new_size" -le 0 ]; then
  echo "sealed staging database is empty" >&2
  exit 8
fi

mv -f "$work" "$live"
sync
installed_size=$(stat -c %s "$live")
if [ "$installed_size" -ne "$new_size" ]; then
  echo "installed database size mismatch: staged=$new_size live=$installed_size" >&2
  exit 9
fi
echo "REMOTE_RESULT=UPDATED imported=$imported bytes=$installed_size"
