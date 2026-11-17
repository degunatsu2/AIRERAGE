#!/usr/bin/env python3
"""PLATEAU（国土交通省の 3D 都市モデル）の建物の OBJ から、背景の街並みに使う軽い形を作る（docs/15 6.51）。

- 面のつながりで建物に分け（同じ位置の頂点はつなぐ）、地面からの高さが --min-height 以上の建物だけを残す。
- 平面直角座標（東・北・高さ。m）を、選んだ中心からの位置にし、地面を高さ 0 にする。
- Unity の OBJ の読み込みは X を反転するので、X を反転して書く（読み込むと東が +X・上が +Y・北が +Z になる）。
- 色・テクスチャは捨てる（ゲームの遠景のシェーダーで塗る）。

使い方: python3 tools/assets/plateau_skyline.py 出力.obj 入力.obj... [--min-height 30] [--center E N] [--ground G]
出典: 国土交通省 Project PLATEAU（3D 都市モデル。東京都 23 区 2020 年度）。加工して作成。
"""
import argparse
import math
import sys


def parse(path):
    verts, faces = [], []
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            if line.startswith('v '):
                _, x, y, z = line.split()[:4]
                verts.append((float(x), float(y), float(z)))
            elif line.startswith('f '):
                idx = [int(p.split('/')[0]) for p in line.split()[1:]]
                idx = [i - 1 if i > 0 else len(verts) + i for i in idx]
                for k in range(1, len(idx) - 1):
                    faces.append((idx[0], idx[k], idx[k + 1]))
    return verts, faces


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
    """同じ位置（1cm）の頂点をまとめ、面でつながる頂点の組（建物）ごとに三角形を返す。"""
    key = {}
    canon = []
    for v in verts:
        k = (round(v[0] * 100), round(v[1] * 100), round(v[2] * 100))
        canon.append(key.setdefault(k, len(key)))
    u = Union(len(key))
    for a, b, c in faces:
        u.join(canon[a], canon[b])
        u.join(canon[a], canon[c])
    groups = {}
    for tri in faces:
        groups.setdefault(u.find(canon[tri[0]]), []).append(tri)
    return groups.values()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('output')
    ap.add_argument('inputs', nargs='+')
    ap.add_argument('--min-height', type=float, default=30.0)
    ap.add_argument('--center', type=float, nargs=2, metavar=('E', 'N'))
    ap.add_argument('--ground', type=float)
    args = ap.parse_args()

    kept = []  # (verts, tris)
    for path in args.inputs:
        verts, faces = parse(path)
        for tris in buildings(verts, faces):
            zs = [verts[i][2] for t in tris for i in t]
            if max(zs) - min(zs) >= args.min_height:
                kept.append((verts, tris, min(zs)))
    if not kept:
        sys.exit('残る建物がない')
    es = [v[0] for verts, tris, _ in kept for t in tris for i in t for v in [verts[i]]]
    ns = [v[1] for verts, tris, _ in kept for t in tris for i in t for v in [verts[i]]]
    cx, cy = args.center if args.center else ((min(es) + max(es)) / 2, (min(ns) + max(ns)) / 2)
    grounds = sorted(g for _, _, g in kept)
    ground = args.ground if args.ground is not None else grounds[len(grounds) // 2]

    out_index = {}
    out_verts = []
    out_tris = []
    for verts, tris, _ in kept:
        for t in tris:
            ids = []
            for i in t:
                e, n, h = verts[i]
                k = (round(e * 100), round(n * 100), round(h * 100))
                if k not in out_index:
                    out_index[k] = len(out_verts)
                    out_verts.append((-(e - cx), h - ground, n - cy))
                ids.append(out_index[k])
            if len(set(ids)) == 3:
                out_tris.append(ids)
    with open(args.output, 'w', encoding='utf-8') as f:
        f.write('# 出典: 国土交通省 Project PLATEAU（3D 都市モデル 東京都 23 区 2020 年度）。加工して作成（tools/assets/plateau_skyline.py）\n')
        f.write(f'# center E={cx:.3f} N={cy:.3f} ground={ground:.3f} min-height={args.min_height}\n')
        for x, y, z in out_verts:
            f.write(f'v {x:.2f} {y:.2f} {z:.2f}\n')
        for a, b, c in out_tris:
            f.write(f'f {a + 1} {b + 1} {c + 1}\n')
    xs = [v[0] for v in out_verts]; ys = [v[1] for v in out_verts]; zs = [v[2] for v in out_verts]
    print(f'{args.output}: 建物 {len(kept)}、頂点 {len(out_verts)}、三角形 {len(out_tris)}、'
          f'X {min(xs):.0f}..{max(xs):.0f} 高さ {min(ys):.0f}..{max(ys):.0f} Z {min(zs):.0f}..{max(zs):.0f}、中心 {cx:.1f} {cy:.1f} 地面 {ground:.1f}')


if __name__ == '__main__':
    main()
