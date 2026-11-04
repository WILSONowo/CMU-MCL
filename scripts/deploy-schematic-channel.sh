#!/usr/bin/env bash
# Activate a preverified signed package, preserving immutable packages and old feeds.
set -euo pipefail
upload="$1"
file="$2"
version="$3"
zip_hash="$4"
feed_hash="$5"
[[ "$upload" =~ ^/tmp/cmumcl-schematic-[0-9]+$ ]]
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
[[ "$file" =~ ^SchematicPreview-[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{16}\.zip$ ]]
[[ "$zip_hash" =~ ^[a-f0-9]{64}$ && "$feed_hash" =~ ^[a-f0-9]{64}$ ]]
[[ "$(sha256sum "$upload/$file" | cut -d ' ' -f 1)" == "$zip_hash" ]]
[[ "$(sha256sum "$upload/latest.json" | cut -d ' ' -f 1)" == "$feed_hash" ]]
root=/opt/pcl-cmu
dest="$root/wwwroot/components/schematic-preview"
backup="$root/backups/schematic-preview-$(date +%Y%m%d-%H%M%S)-$$"
mkdir -p "$dest" "$backup"
exec 9>"$root/schematic-preview-publish.lock"
flock -x 9
python3 - "$upload/latest.json" "$dest/latest.json" "$version" "$file" "$zip_hash" <<'PY'
import base64,json,pathlib,sys
incoming,current,version,filename,sha=sys.argv[1:]
def read(path): return json.loads(base64.b64decode(json.loads(pathlib.Path(path).read_bytes())['payload']))
r=read(incoming)
assert r['version']==version and r['sha256']==sha
assert r['url']=='https://csumcl.mcapcs.cn/components/schematic-preview/'+filename
if pathlib.Path(current).exists():
    old=read(current)
    assert tuple(map(int,version.split('.'))) >= tuple(map(int,old['version'].split('.'))), 'Refusing downgrade'
    if old['version']==version:
        assert pathlib.Path(incoming).read_bytes()==pathlib.Path(current).read_bytes(), 'Same-version manifest collision'
PY
if [ -f "$dest/$file" ]; then
    [[ "$(sha256sum "$dest/$file" | cut -d ' ' -f 1)" == "$zip_hash" ]]
else
    install -m 644 "$upload/$file" "$dest/.$file.tmp"
    mv "$dest/.$file.tmp" "$dest/$file"
fi
if [ -f "$dest/latest.json" ]; then cp -a "$dest/latest.json" "$backup/latest.json"; fi
install -m 644 "$upload/latest.json" "$dest/.latest-$$.tmp"
mv "$dest/.latest-$$.tmp" "$dest/latest.json"
[[ "$(sha256sum "$dest/latest.json" | cut -d ' ' -f 1)" == "$feed_hash" ]]
printf 'COMPONENT_PUBLISHED version=%s file=%s backup=%s\n' "$version" "$file" "$backup"
