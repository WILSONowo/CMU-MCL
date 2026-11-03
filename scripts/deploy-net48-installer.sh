#!/bin/sh
set -eu

file="$1"
expected_sha="$2"
target_dir=/opt/pcl-cmu/wwwroot/downloads

mkdir -p "$target_dir"
chmod 755 "$target_dir"
install -m 644 "/tmp/$file" "$target_dir/$file"
actual=$(sha256sum "$target_dir/$file" | awk '{print $1}')
if [ "$actual" != "$expected_sha" ]; then
    echo "INSTALLER_HASH_MISMATCH expected=$expected_sha actual=$actual" >&2
    exit 1
fi
rm -f "/tmp/$file"
echo "INSTALLER_PUBLISHED $file $actual"
