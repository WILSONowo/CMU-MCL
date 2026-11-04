"""Verify an already signed component release without private signing material."""
import base64
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import tempfile
import zipfile


def signature(value, data, key, directory, name):
    assert value['algorithm'] == 'ECDSA-P256-SHA256'
    assert value['format'] == 'ieee-p1363-base64'
    raw = base64.b64decode(value['value'], validate=True)
    assert len(raw) == 64

    def integer(b):
        b = b.lstrip(b'\0') or b'\0'
        if b[0] & 128:
            b = b'\0' + b
        return b'\x02' + bytes([len(b)]) + b

    body = integer(raw[:32]) + integer(raw[32:])
    sig = directory / (name + '.sig')
    source = directory / (name + '.data')
    sig.write_bytes(b'\x30' + bytes([len(body)]) + body)
    source.write_bytes(data)
    subprocess.run(['openssl', 'dgst', '-sha256', '-verify', str(key),
                    '-signature', str(sig), str(source)], check=True)


def verify(root):
    feed = root / 'latest.json'
    assert feed.stat().st_size <= 128 * 1024
    envelope = json.loads(feed.read_bytes())
    payload = base64.b64decode(envelope['payload'], validate=True)
    key = pathlib.Path(__file__).resolve().parents[1] / 'CMUmcl/Content/SchematicPreviewPublicKey.pem'
    with tempfile.TemporaryDirectory() as temp:
        directory = pathlib.Path(temp)
        signature(envelope['signature'], payload, key, directory, 'payload')
        release = json.loads(payload)
        assert release['formatVersion'] == 1
        assert release['component'] == 'schematic-preview' and release['apiVersion'] == 2
        assert re.fullmatch(r'\d+\.\d+\.\d+', release['version'])
        assert release['minLauncherVersion'] == '0.3.48.12'
        filename = f"SchematicPreview-{release['version']}-{release['sha256'][:16]}.zip"
        assert release['url'] == 'https://csumcl.mcapcs.cn/components/schematic-preview/' + filename
        archive = root / filename
        assert 0 < release['size'] == archive.stat().st_size <= 128 * 1024 * 1024
        data = archive.read_bytes()
        assert hashlib.sha256(data).hexdigest() == release['sha256']
        signature(release['archiveSignature'], data, key, directory, 'archive')
        with zipfile.ZipFile(archive) as z:
            assert hashlib.sha256(z.read('SchematicPreviewWorker.exe')).hexdigest() == release['entrySha256']
            manifest = json.loads(z.read('component.json'))
            assert manifest['version'] == release['version'] and manifest['apiVersion'] == 2
            assert {'Source.zip', 'LICENSE', 'NOTICE', 'SOURCE.txt'}.issubset(z.namelist())
    print('VERIFIED', release['version'], filename, release['sha256'])


if __name__ == '__main__':
    verify(pathlib.Path(sys.argv[1]).resolve())
