#!/usr/bin/env bash
# Run on the resource server. Installs an already-uploaded feed and package.
set -euo pipefail

version="$1"
file="$2"
expected_zip_sha="$3"
feed_source="$4"
expected_feed_sha="$5"

cleanup() { rm -f "/tmp/$file" "$feed_source"; }
trap cleanup EXIT

root=/opt/pcl-cmu
dest="$root/wwwroot/update/windows-x86_64/net48"
ts=$(date +%Y%m%d-%H%M%S)
backup="$root/backups/net48-feed-$ts"

mkdir -p "$dest" "$backup"

if [ -f "$dest/latest.json" ]; then
    cp -a "$dest/latest.json" "$backup/latest.json"
fi

if [ -f "$dest/$file" ]; then
    existing=$(sha256sum "$dest/$file" | awk '{print $1}')
    if [ "$existing" != "$expected_zip_sha" ]; then
        echo "IMMUTABLE_COLLISION $file" >&2
        exit 1
    fi
else
    install -m 644 "/tmp/$file" "$dest/$file"
fi

actual_zip=$(sha256sum "$dest/$file" | awk '{print $1}')
if [ "$actual_zip" != "$expected_zip_sha" ]; then
    echo "ZIP_HASH_MISMATCH expected=$expected_zip_sha actual=$actual_zip" >&2
    exit 1
fi

if [ -f "$dest/latest.json" ]; then
    current_feed_sha=$(sha256sum "$dest/latest.json" | awk '{print $1}')
    if [ "$current_feed_sha" = "$expected_feed_sha" ]; then
        echo "ALREADY_PUBLISHED version=$version file=$file"
        exit 0
    fi
fi

if [ -f "$dest/latest.json" ]; then
    current_version=$(python3 - "$dest/latest.json" <<'PY'
import base64, json, sys
try:
    envelope = json.load(open(sys.argv[1]))
    release = json.loads(base64.b64decode(envelope['payload']))
    print(release['version'])
except Exception:
    print('0.0.0')
PY
)
    python3 - "$current_version" "$version" <<'PY'
import sys
def parse(value):
    return tuple(int(part) for part in value.split('.'))
if parse(sys.argv[2]) <= parse(sys.argv[1]):
    print(f'REFUSING_DOWNGRADE current={sys.argv[1]} incoming={sys.argv[2]}', file=sys.stderr)
    raise SystemExit(1)
PY
fi

temporary="$dest/.latest-$ts.tmp"
cp "$feed_source" "$temporary"
chmod 644 "$temporary"
mv "$temporary" "$dest/latest.json"

actual_feed=$(sha256sum "$dest/latest.json" | awk '{print $1}')
if [ "$actual_feed" != "$expected_feed_sha" ]; then
    echo "FEED_HASH_MISMATCH expected=$expected_feed_sha actual=$actual_feed" >&2
    exit 1
fi

echo "PUBLISHED version=$version file=$file sha256=$actual_zip feed_sha256=$actual_feed backup=$backup"
