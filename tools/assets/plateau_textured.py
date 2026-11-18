#!/usr/bin/env python3
"""PLATEAU（国土交通省の 3D 都市モデル）の LOD2 の建物を、外壁の写真（テクスチャ）つきで背景用に書き出す（docs/15 6.53）。

2026-11-18 のユーザーの要望（背景がほぼシルエットに見える。ある程度リアルでよく、加工は最低限に）に合わせて、形は減らさず、
テクスチャを残す。前の tools/assets/plateau_skyline.py（色とテクスチャを捨てる）の代わり。

- 区画の OBJ・MTL と、残す建物が使うテクスチャだけを、配布の zip から範囲を指定して取り出す（remote_zip.py。--cache に保存し、
  2 回目からは取り直さない）。
- 面のつながりで建物に分け（同じ位置の頂点はつなぐ）、地面からの高さが --min-height 以上の建物だけを残す。
- テクスチャは、使っている範囲だけを切り出し、外壁の 1 画素がおよそ --texel m になる大きさへ縮めて、1 枚のアトラス（--atlas 画素四方の
  JPG）にまとめ、UV を付け替える（入りきらなければ、入るまで 1 画素あたりの大きさを広げる）。
- 平面直角座標（東・北・高さ。m）を、中心からの位置にし、地面を高さ 0 にする。建物ごとの根元の高さは土地の起伏で ±10m ほど違うので、
  --snap-ground なら建物ごとに根元を地面（--ground）にそろえる（道路の地面に立たせたとき、浮いたり沈んだりしないように）。Unity の OBJ の読み込みは X を反転するので、X を反転して書く
  （読み込むと東が +X・上が +Y・北が +Z）。

- 夜の明かり（--night）: 外壁の写真の窓（まわりより暗く青みのある所）を見つけ、窓ごとに点ける・点けないを決めて、建物（テクスチャ）
  ごとの色（暖かい白・ネオンのマゼンタ・シアン・紫・橙）で光らせた絵を、同じ並びのアトラスに書く（2026-11-18 の要望: 夜の明かりを
  窓と同期させ、ネオン風に光らせる）。シェーダーが縦の面にだけ足す（屋根は光らない）。

使い方: python3 -I tools/assets/plateau_textured.py 出力の名前 --cache フォルダ --mesh 53394525 53394526 ... \
            [--min-height 50] [--center E N] [--ground G] [--texel 0.6] [--atlas 4096] [--night]
  出力: 出力の名前.obj と 出力の名前_Atlas.jpg（--night なら 出力の名前_AtlasNight.jpg も）
出典: 国土交通省 Project PLATEAU（3D 都市モデル。東京都 23 区 2020 年度）。加工して作成。
"""
import argparse
import concurrent.futures
import importlib.util
import json
import math
import os
import re
import sys

import numpy as np
from PIL import Image, ImageFilter

URL = ('https://gsic-opendata.s3.ap-northeast-1.amazonaws.com/national-gov/mlit/city-bureau/3d-city-model/2020/'
       'plateau-tokyo23ku/13100_tokyo23-ku_2020_obj_3_op.zip')
# 区画のまわりの余白（画素）。縁の色を外へ伸ばして埋める。遠くでミップマップが隣の区画の色を混ぜる「色のにじみ」を防ぐ
# （2026-11-18 の P1。前は 4 画素で、遠くの細かい建物の色が混ざっていた）。--padding で変える。
PADDING = 16


def load_remote_zip():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'remote_zip.py')
    spec = importlib.util.spec_from_file_location('remote_zip', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def parse_obj(path):
    verts, uvs, faces = [], [], []
    material = None
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            if line.startswith('v '):
                _, x, y, z = line.split()[:4]
                verts.append((float(x), float(y), float(z)))
            elif line.startswith('vt '):
                p = line.split()
                uvs.append((float(p[1]), float(p[2])))
            elif line.startswith('usemtl '):
                material = line.split(None, 1)[1].strip()
            elif line.startswith('f '):
                corners = []
                for part in line.split()[1:]:
                    a = part.split('/')
                    vi = int(a[0])
                    ti = int(a[1]) if len(a) > 1 and a[1] else 0
                    corners.append((vi - 1 if vi > 0 else len(verts) + vi,
                                    (ti - 1 if ti > 0 else len(uvs) + ti) if ti else -1))
                for k in range(1, len(corners) - 1):
                    faces.append((corners[0], corners[k], corners[k + 1], material))
    return verts, uvs, faces


def parse_mtl(path):
    textures = {}
    current = None
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if line.startswith('newmtl'):
                current = line.split(None, 1)[1].strip()
            elif line.startswith('map_Kd') and current:
                textures[current] = line.split(None, 1)[1].strip().replace('\\', '/').split('/')[-1]
    return textures


class Union:
    def __init__(self, n):
        self.p = list(range(n))

    def find(self, a):
        while self.p[a] != a:
            self.p[a] = self.p[self.p[a]]
            a = self.p[a]
        return a

    def join(self, a, b):
        a, b = self.find(a), self.find(b)
        if a != b:
            self.p[b] = a


def buildings(verts, faces):
    """同じ位置（1cm）の頂点をまとめ、面でつながる建物ごとに三角形を返す。"""
    key = {}
    canon = []
    for v in verts:
        canon.append(key.setdefault((round(v[0] * 100), round(v[1] * 100), round(v[2] * 100)), len(key)))
    u = Union(len(key))
    for a, b, c, _ in faces:
        u.join(canon[a[0]], canon[b[0]])
        u.join(canon[a[0]], canon[c[0]])
    groups = {}
    for face in faces:
        groups.setdefault(u.find(canon[face[0][0]]), []).append(face)
    return list(groups.values())


def tri_area3(a, b, c):
    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    cx, cy, cz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
    return 0.5 * math.sqrt(cx * cx + cy * cy + cz * cz)


def tri_area2(a, b, c):
    return 0.5 * abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1]))


def pack(cells, size):
    """棚に並べる（高い順）。入れば各セルの位置、入らなければ None。"""
    x = y = shelf = 0
    placed = {}
    for key, (w, h) in sorted(cells.items(), key=lambda kv: -kv[1][1]):
        w2, h2 = w + PADDING * 2, h + PADDING * 2
        if x + w2 > size:
            x, y, shelf = 0, y + shelf, 0
        if y + h2 > size or w2 > size:
            return None
        placed[key] = (x + PADDING, y + PADDING)
        x += w2
        shelf = max(shelf, h2)
    return placed


# 夜の明かりの色（建物ごとに 1 つ選ぶ。暖かい白が多め、ネオンの色を混ぜる）と、窓ごとに混ぜる 2 つ目の色。
NIGHT_PALETTE = [
    ((1.0, 0.80, 0.48), 0.30),   # 暖かい白（事務所の明かり）
    ((0.72, 0.88, 1.0), 0.12),   # 青白い明かり
    ((1.0, 0.28, 0.86), 0.16),   # ネオンのマゼンタ
    ((0.22, 0.95, 1.0), 0.16),   # ネオンのシアン
    ((0.66, 0.36, 1.0), 0.14),   # ネオンの紫
    ((1.0, 0.58, 0.16), 0.12),   # ネオンの橙
]


def night_lights(atlas, placed, cells):
    """外壁の写真から窓を見つけ、窓ごとに点けた明かりの絵（同じ並び）を返す。"""
    rgb = np.asarray(atlas, dtype=np.float32) / 255.0
    lum = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    local = np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(3)), dtype=np.float32) / 255.0
    # 窓のガラス: まわりより暗い所と、青みのある所。
    glass = np.clip((local - lum) * 7.0 + (rgb[..., 2] - rgb[..., 0]) * 2.5 - 0.05, 0.0, 1.0) ** 0.8
    out = np.zeros_like(rgb)
    weights = np.array([w for _, w in NIGHT_PALETTE])
    weights /= weights.sum()
    for index, (key, (x, y)) in enumerate(sorted(placed.items())):
        cw, ch = cells[key]
        # 決まった種（同じ入力なら毎回同じ絵）。
        rng = np.random.default_rng(index * 7919 + 17)
        main = np.array(NIGHT_PALETTE[rng.choice(len(NIGHT_PALETTE), p=weights)][0], dtype=np.float32)
        accent = np.array(NIGHT_PALETTE[rng.choice(len(NIGHT_PALETTE), p=weights)][0], dtype=np.float32)
        lit_ratio = rng.uniform(0.45, 0.75)
        # 窓のかたまり（横 3 画素・縦 2 画素ほど。1 画素がおよそ 0.7m）ごとに点ける・点けないと色を決める。
        by = np.arange(ch)[:, None] // 2
        bx = np.arange(cw)[None, :] // 3
        hashed = ((bx * 73856093) ^ (by * 19349663) ^ (index * 83492791)) & 0xFFFF
        lit = (hashed / 65535.0) < lit_ratio
        use_accent = (((by * 2654435761) ^ index) & 0xFF) < 40
        color = np.where(use_accent[..., None], accent, main)
        brightness = 0.65 + 0.35 * ((hashed * 31 & 0xFF) / 255.0)
        region = glass[y:y + ch, x:x + cw]
        out[y:y + ch, x:x + cw] = region[..., None] * lit[..., None] * color * brightness[..., None]
    return Image.fromarray((np.clip(out, 0.0, 1.0) * 255).astype(np.uint8))


def main():
    global PADDING
    ap = argparse.ArgumentParser()
    ap.add_argument('output')
    ap.add_argument('--cache', required=True)
    ap.add_argument('--mesh', nargs='+', required=True)
    ap.add_argument('--min-height', type=float, default=50.0)
    ap.add_argument('--center', type=float, nargs=2, metavar=('E', 'N'))
    ap.add_argument('--ground', type=float)
    ap.add_argument('--texel', type=float, default=0.6, help='外壁の 1 画素の大きさ（m）の目安')
    ap.add_argument('--atlas', type=int, default=4096)
    ap.add_argument('--night', action='store_true', help='夜の窓の明かりの絵も書く')
    ap.add_argument('--snap-ground', action='store_true', help='建物ごとに根元を地面にそろえる')
    ap.add_argument('--padding', type=int, default=16, help='区画のまわりの余白（画素。縁の色を伸ばす）')
    args = ap.parse_args()
    PADDING = args.padding

    rz = load_remote_zip()
    os.makedirs(args.cache, exist_ok=True)
    listing = os.path.join(args.cache, 'entries.json')
    if not os.path.exists(listing):
        json.dump(rz.entries(URL), open(listing, 'w'))
    entries = {e['name']: e for e in json.load(open(listing))}

    def fetch(name):
        dest = os.path.join(args.cache, name.split('/lod2/')[1])
        if not os.path.exists(dest):
            rz.extract(URL, entries[name], dest)
        return dest

    prefix = '13100_tokyo23-ku_2020_obj_3_op/bldg/lod2/'
    kept = []  # (mesh, verts, uvs, faces, textures)
    for mesh in args.mesh:
        folder = f'{prefix}{mesh}_bldg_6677_obj/'
        obj = fetch(f'{folder}{mesh}_bldg_6677.obj')
        mtl = fetch(f'{folder}materials.mtl')
        verts, uvs, faces = parse_obj(obj)
        textures = parse_mtl(mtl)
        for group in buildings(verts, faces):
            zs = [verts[c[0]][2] for f in group for c in f[:3]]
            if max(zs) - min(zs) >= args.min_height:
                kept.append((mesh, verts, uvs, group, textures, min(zs)))
    if not kept:
        sys.exit('残る建物がない')

    # 材質ごとの、外壁の広さ・UV の広さと範囲（残す建物の面だけ）。
    usage = {}
    for mesh, verts, uvs, group, textures, _ in kept:
        for a, b, c, material in group:
            if material is None or material not in textures or min(a[1], b[1], c[1]) < 0:
                continue
            key = (mesh, textures[material])
            u = usage.setdefault(key, {'world': 0.0, 'uv': 0.0, 'box': [1e9, 1e9, -1e9, -1e9]})
            ta, tb, tc = uvs[a[1]], uvs[b[1]], uvs[c[1]]
            u['world'] += tri_area3(verts[a[0]], verts[b[0]], verts[c[0]])
            u['uv'] += tri_area2(ta, tb, tc)
            box = u['box']
            for t in (ta, tb, tc):
                box[0] = min(box[0], t[0]); box[1] = min(box[1], t[1])
                box[2] = max(box[2], t[0]); box[3] = max(box[3], t[1])

    # 使うテクスチャだけを取り出す（並べて）。
    names = [f'{prefix}{mesh}_bldg_6677_obj/materials_textures/{tex}' for mesh, tex in usage]
    missing = [n for n in names if n in entries]
    with concurrent.futures.ThreadPoolExecutor(4) as pool:
        list(pool.map(fetch, missing))
    print(f'建物 {len(kept)}、テクスチャ {len(usage)}', file=sys.stderr)

    # 切り出す範囲と、縮めた大きさ。
    sources = {}
    for (mesh, tex), u in usage.items():
        path = os.path.join(args.cache, f'{mesh}_bldg_6677_obj/materials_textures/{tex}')
        if not os.path.exists(path):
            continue
        image = Image.open(path).convert('RGB')
        w, h = image.size
        u0, v0 = max(0.0, u['box'][0]), max(0.0, u['box'][1])
        u1, v1 = min(1.0, u['box'][2]), min(1.0, u['box'][3])
        if u1 <= u0 or v1 <= v0:
            continue
        crop = (int(math.floor(u0 * w)), int(math.floor((1 - v1) * h)), int(math.ceil(u1 * w)), int(math.ceil((1 - v0) * h)))
        crop = (crop[0], crop[1], max(crop[2], crop[0] + 1), max(crop[3], crop[1] + 1))
        sources[(mesh, tex)] = (image, crop, (u0, v0, u1, v1), u)

    texel = args.texel
    while True:
        cells = {}
        for key, (image, crop, uvbox, u) in sources.items():
            cw, ch = crop[2] - crop[0], crop[3] - crop[1]
            w, h = image.size
            # 元の 1 画素あたりの外壁の大きさ（m）から、縮める割合を決める（大きくはしない）。
            texels = max(u['uv'], 1e-9) * w * h
            meters_per_texel = math.sqrt(max(u['world'], 1e-6) / texels)
            scale = min(1.0, meters_per_texel / texel)
            cells[key] = (max(2, int(round(cw * scale))), max(2, int(round(ch * scale))))
        placed = pack(cells, args.atlas)
        if placed is not None:
            break
        texel *= 1.15
    atlas = Image.new('RGB', (args.atlas, args.atlas), (128, 128, 128))
    for key, (x, y) in placed.items():
        image, crop, _, _ = sources[key]
        cw, ch = cells[key]
        piece = image.crop(crop).resize((cw, ch), Image.LANCZOS)
        # 縁の画素をそのまま外へ伸ばして余白を埋める（にじみ出し。縮めた絵の色が隣のセルににじまないように）。
        padded = Image.fromarray(np.pad(np.asarray(piece), ((PADDING, PADDING), (PADDING, PADDING), (0, 0)), mode='edge'))
        atlas.paste(padded, (x - PADDING, y - PADDING))
    atlas.save(f'{args.output}_Atlas.jpg', quality=88)
    if args.night:
        night_lights(atlas, placed, cells).save(f'{args.output}_AtlasNight.jpg', quality=90)

    # 位置と UV を書く。
    es = [verts[c[0]][0] for _, verts, _, group, _, _ in kept for f in group for c in f[:3]]
    ns = [verts[c[0]][1] for _, verts, _, group, _, _ in kept for f in group for c in f[:3]]
    cx, cy = args.center if args.center else ((min(es) + max(es)) / 2, (min(ns) + max(ns)) / 2)
    grounds = sorted(g for *_, g in kept)
    ground = args.ground if args.ground is not None else grounds[len(grounds) // 2]
    out_v, out_t, out_f = [], [], []
    v_index, t_index = {}, {}
    plain = (0.5 / args.atlas, 0.5 / args.atlas)
    for mesh, verts, uvs, group, textures, base in kept:
        lift = ground - base if args.snap_ground else 0.0
        for a, b, c, material in group:
            key = (mesh, textures.get(material, '')) if material else None
            ids = []
            for vi, ti in (a, b, c):
                e, n, hgt = verts[vi]
                vk = (round(e * 100), round(n * 100), round((hgt + lift) * 100))
                if vk not in v_index:
                    v_index[vk] = len(out_v)
                    out_v.append((-(e - cx), hgt + lift - ground, n - cy))
                if key in placed and ti >= 0:
                    x, y = placed[key]
                    cw, ch = cells[key]
                    u0, v0, u1, v1 = sources[key][2]
                    tu, tv = uvs[ti]
                    fu = min(max((tu - u0) / max(u1 - u0, 1e-9), 0.0), 1.0)
                    fv = min(max((tv - v0) / max(v1 - v0, 1e-9), 0.0), 1.0)
                    # 画像の上が v=1。アトラスの中のセルの位置（左上が原点）から OBJ の v（下が 0）へ。
                    uv = ((x + fu * cw) / args.atlas, 1.0 - (y + (1.0 - fv) * ch) / args.atlas)
                else:
                    uv = plain
                tk = (round(uv[0] * 1e6), round(uv[1] * 1e6))
                if tk not in t_index:
                    t_index[tk] = len(out_t)
                    out_t.append(uv)
                ids.append((v_index[vk], t_index[tk]))
            if len({i[0] for i in ids}) == 3:
                out_f.append(ids)
    with open(f'{args.output}.obj', 'w', encoding='utf-8') as f:
        f.write('# 出典: 国土交通省 Project PLATEAU（3D 都市モデル 東京都 23 区 2020 年度）。加工して作成（tools/assets/plateau_textured.py）\n')
        f.write(f'# center E={cx:.3f} N={cy:.3f} ground={ground:.3f} snap-ground={args.snap_ground} min-height={args.min_height} texel={texel:.3f}m '
                f'atlas={os.path.basename(args.output)}_Atlas.jpg\n')
        for x, y, z in out_v:
            f.write(f'v {x:.2f} {y:.2f} {z:.2f}\n')
        for u, v in out_t:
            f.write(f'vt {u:.6f} {v:.6f}\n')
        for face in out_f:
            f.write('f ' + ' '.join(f'{vi + 1}/{ti + 1}' for vi, ti in face) + '\n')
    print(f'{args.output}.obj: 建物 {len(kept)}、頂点 {len(out_v)}、UV {len(out_t)}、三角形 {len(out_f)}、'
          f'テクスチャ {len(placed)}（1 画素 {texel:.2f}m、{args.atlas} 四方）、中心 {cx:.1f} {cy:.1f} 地面 {ground:.1f}')


if __name__ == '__main__':
    main()
