#!/usr/bin/env python3
"""HTTP の範囲指定で、大きな zip の目録を読み、必要なファイルだけを取り出す（全体はダウンロードしない。docs/15 6.51・6.53）。

使い方（目録を JSON に書く）: python3 -I tools/assets/remote_zip.py URL 目録.json
関数: entries(url) で目録、extract(url, entry, dest) で 1 つを取り出す。PLATEAU の OBJ の配布（約 2.2GB）から必要な区画だけを読むのに使う。
"""
import struct, sys, time, urllib.error, urllib.request, zlib, os, json

def fetch(url, start, end, attempts=5):
    # 接続が切られたら、間隔を空けて取り直す（並べて取り出すと、ときどき切られる）。
    for attempt in range(attempts):
        try:
            req = urllib.request.Request(url, headers={'Range': f'bytes={start}-{end}'})
            with urllib.request.urlopen(req, timeout=120) as r:
                return r.read()
        except (OSError, urllib.error.URLError) as error:
            if attempt == attempts - 1:
                raise
            print(f'取り直す（{error}）', file=sys.stderr)
            time.sleep(2 ** attempt)

def size_of(url):
    req = urllib.request.Request(url, method='HEAD')
    with urllib.request.urlopen(req, timeout=60) as r:
        return int(r.headers['Content-Length'])

def entries(url):
    total = size_of(url)
    tail = fetch(url, max(0, total - 65536 - 22), total - 1)
    i = tail.rfind(b'PK\x05\x06')
    cd_size, cd_off = struct.unpack('<II', tail[i + 12:i + 20])
    count = struct.unpack('<H', tail[i + 10:i + 12])[0]
    zip64 = cd_off == 0xFFFFFFFF or cd_size == 0xFFFFFFFF or count == 0xFFFF
    if zip64:
        j = tail.rfind(b'PK\x06\x07')
        eocd64 = struct.unpack('<Q', tail[j + 8:j + 16])[0]
        rec = fetch(url, eocd64, eocd64 + 56 - 1)
        count, cd_size, cd_off = struct.unpack('<QQQ', rec[32:56])
    cd = fetch(url, cd_off, cd_off + cd_size - 1)
    out = []
    p = 0
    while p < len(cd) and cd[p:p + 4] == b'PK\x01\x02':
        method = struct.unpack('<H', cd[p + 10:p + 12])[0]
        csize, usize = struct.unpack('<II', cd[p + 20:p + 28])
        nlen, elen, clen = struct.unpack('<HHH', cd[p + 28:p + 34])
        off = struct.unpack('<I', cd[p + 42:p + 46])[0]
        name = cd[p + 46:p + 46 + nlen].decode('utf-8', 'replace')
        extra = cd[p + 46 + nlen:p + 46 + nlen + elen]
        q = 0
        while q + 4 <= len(extra):
            hid, hlen = struct.unpack('<HH', extra[q:q + 4])
            if hid == 1:
                vals = extra[q + 4:q + 4 + hlen]
                k = 0
                if usize == 0xFFFFFFFF: usize = struct.unpack('<Q', vals[k:k + 8])[0]; k += 8
                if csize == 0xFFFFFFFF: csize = struct.unpack('<Q', vals[k:k + 8])[0]; k += 8
                if off == 0xFFFFFFFF: off = struct.unpack('<Q', vals[k:k + 8])[0]; k += 8
            q += 4 + hlen
        out.append({'name': name, 'method': method, 'csize': csize, 'usize': usize, 'offset': off})
        p += 46 + nlen + elen + clen
    return out

def extract(url, e, dest):
    head = fetch(url, e['offset'], e['offset'] + 30 - 1)
    nlen, elen = struct.unpack('<HH', head[26:30])
    start = e['offset'] + 30 + nlen + elen
    data = fetch(url, start, start + e['csize'] - 1) if e['csize'] else b''
    if e['method'] == 8:
        data = zlib.decompress(data, -15)
    os.makedirs(os.path.dirname(dest) or '.', exist_ok=True)
    with open(dest, 'wb') as f:
        f.write(data)

if __name__ == '__main__':
    url = sys.argv[1]
    es = entries(url)
    json.dump(es, open(sys.argv[2], 'w'))
    print(len(es))
