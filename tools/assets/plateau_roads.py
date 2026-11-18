#!/usr/bin/env python3
"""PLATEAU の道路（CityGML の tran。LOD1 の道路の面）から、背景の街の地面の絵（真上から見た道路の地図）を作る（docs/15 6.54）。

2026-11-18 のユーザーの要望（ビル街の背景の下側で建物がぶつ切りになっている。道路があれば描いてほしい）に合わせて、実在の西新宿の
建物を道路の上に立たせる。斜めから遠くに見えるだけなので、道路は面の形ではなく、真上から見た地図の絵にして地面の板に貼る。

- 配布の CityGML の zip から、2 次メッシュの道路のファイル（例 533945_tran_6697_op.gml）だけを取り出す（remote_zip.py。--cache に保存）。
- 道路の面（緯度・経度。EPSG:6697）を、平面直角座標 IX 系（EPSG:6677。PLATEAU の OBJ と同じ東・北の m）へ変換する
  （国土地理院の Gauss-Krüger 投影の式。河瀬 2011）。
- 中心（--center E N）から ±--half m の四角を --size 画素四方の絵にし、地面（歩道・敷地）の色に道路を塗る（穴は地面の色に戻す）。
- 道路の地図の絵（_Ground.jpg）を書く（昼も夜も同じ絵。夜は暗い色合いで描く。夜の道路の明かりの絵は 2026-11-18 にやめた）。

使い方: python3 -I tools/assets/plateau_roads.py 出力の名前 --cache フォルダ --mesh2 533945 --center -12196.9 -34315.7 [--half 1200] [--size 2048]
出典: 国土交通省 Project PLATEAU（3D 都市モデル。東京都 23 区 2020 年度）。加工して作成。
"""
import argparse
import importlib.util
import json
import math
import os
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw, ImageFilter

URL = ('https://gsic-opendata.s3.ap-northeast-1.amazonaws.com/national-gov/mlit/city-bureau/3d-city-model/2020/'
       'plateau-tokyo23ku/13100_tokyo23-ku_2020_citygml_3_2_op.zip')
GML = '{http://www.opengis.net/gml}'

GROUND = (118, 120, 124)      # 歩道・敷地（明るいコンクリート）
ASPHALT = (52, 55, 60)        # 車道


def load_remote_zip():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'remote_zip.py')
    spec = importlib.util.spec_from_file_location('remote_zip', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def plane_ix(lat, lon):
    """緯度・経度（度。JGD2011）を平面直角座標 IX 系の（東, 北）（m）へ。国土地理院の Gauss-Krüger の式（n の 5 次まで）。"""
    a = 6378137.0
    f = 1 / 298.257222101
    n = f / (2 - f)
    m0 = 0.9999
    phi0 = math.radians(36.0)
    lam0 = math.radians(139.0 + 50.0 / 60.0)
    phi = math.radians(lat)
    lam = math.radians(lon)
    alpha = [0.0,
             n / 2 - 2 * n ** 2 / 3 + 5 * n ** 3 / 16 + 41 * n ** 4 / 180 - 127 * n ** 5 / 288,
             13 * n ** 2 / 48 - 3 * n ** 3 / 5 + 557 * n ** 4 / 1440 + 281 * n ** 5 / 630,
             61 * n ** 3 / 240 - 103 * n ** 4 / 140 + 15061 * n ** 5 / 26880,
             49561 * n ** 4 / 161280 - 179 * n ** 5 / 168,
             34729 * n ** 5 / 80640]
    A = [1 + n ** 2 / 4 + n ** 4 / 64,
         -3 / 2 * (n - n ** 3 / 8 - n ** 5 / 64),
         15 / 16 * (n ** 2 - n ** 4 / 4),
         -35 / 48 * (n ** 3 - 5 * n ** 5 / 16),
         315 / 512 * n ** 4,
         -693 / 1280 * n ** 5]
    abar = m0 * a / (1 + n) * A[0]
    sbar = m0 * a / (1 + n) * (A[0] * phi0 + sum(A[j] * math.sin(2 * j * phi0) for j in range(1, 6)))
    k = 2 * math.sqrt(n) / (1 + n)
    t = math.sinh(math.atanh(math.sin(phi)) - k * math.atanh(k * math.sin(phi)))
    tbar = math.sqrt(1 + t * t)
    lc = math.cos(lam - lam0)
    ls = math.sin(lam - lam0)
    xi = math.atan2(t, lc)
    eta = math.atanh(ls / tbar)
    north = abar * (xi + sum(alpha[j] * math.sin(2 * j * xi) * math.cosh(2 * j * eta) for j in range(1, 6))) - sbar
    east = abar * (eta + sum(alpha[j] * math.cos(2 * j * xi) * math.sinh(2 * j * eta) for j in range(1, 6)))
    return east, north


def rings(path):
    """道路の面ごとに（外周, [穴...]）を返す（緯度・経度のまま）。"""
    for _, element in ET.iterparse(path, events=('end',)):
        if element.tag != GML + 'Polygon':
            continue
        exterior = element.find(f'{GML}exterior/{GML}LinearRing/{GML}posList')
        holes = element.findall(f'{GML}interior/{GML}LinearRing/{GML}posList')
        if exterior is not None:
            yield parse_pos(exterior.text), [parse_pos(h.text) for h in holes]
        element.clear()


def parse_pos(text):
    values = [float(v) for v in text.split()]
    return [(values[i], values[i + 1]) for i in range(0, len(values) - 2, 3)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('output')
    ap.add_argument('--cache', required=True)
    ap.add_argument('--mesh2', nargs='+', required=True, help='2 次メッシュの番号（例 533945）')
    ap.add_argument('--center', type=float, nargs=2, required=True, metavar=('E', 'N'))
    ap.add_argument('--half', type=float, default=1200.0, help='中心からの範囲（m）')
    ap.add_argument('--size', type=int, default=2048)
    args = ap.parse_args()

    rz = load_remote_zip()
    os.makedirs(args.cache, exist_ok=True)
    listing = os.path.join(args.cache, 'entries.json')
    if not os.path.exists(listing):
        json.dump(rz.entries(URL), open(listing, 'w'))
    entries = {e['name']: e for e in json.load(open(listing))}

    cx, cy = args.center
    scale = args.size / (2 * args.half)

    def to_pixel(lat, lon):
        e, n = plane_ix(lat, lon)
        # 画像の左が西、上が北。
        return ((e - (cx - args.half)) * scale, ((cy + args.half) - n) * scale)

    day = Image.new('RGB', (args.size, args.size), GROUND)
    draw_day = ImageDraw.Draw(day)
    count = 0
    for mesh in args.mesh2:
        name = f'13100_tokyo23-ku_2020_citygml_3_2_op/udx/tran/{mesh}_tran_6697_op.gml'
        path = os.path.join(args.cache, os.path.basename(name))
        if not os.path.exists(path):
            rz.extract(URL, entries[name], path)
        for exterior, holes in rings(path):
            points = [to_pixel(lat, lon) for lat, lon in exterior]
            xs = [p[0] for p in points]
            ys = [p[1] for p in points]
            if max(xs) < 0 or min(xs) > args.size or max(ys) < 0 or min(ys) > args.size or len(points) < 3:
                continue
            draw_day.polygon(points, fill=ASPHALT)
            for hole in holes:
                hole_points = [to_pixel(lat, lon) for lat, lon in hole]
                if len(hole_points) >= 3:
                    draw_day.polygon(hole_points, fill=GROUND)
            count += 1
    # 縁を少しなじませる（遠くで線がちらつかないように）。
    day = day.filter(ImageFilter.GaussianBlur(0.6))
    day.save(f'{args.output}_Ground.jpg', quality=88)

    print(f'{args.output}: 道路の面 {count}、{args.size} 四方（1 画素 {1 / scale:.2f}m）、中心 {cx} {cy}、範囲 ±{args.half}m')


if __name__ == '__main__':
    main()
