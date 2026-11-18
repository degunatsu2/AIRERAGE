#!/usr/bin/env python3
"""PLATEAU の OBJ から、つぶれた三角形と、同じ面の上で別の三角形に重なっている小さい三角形を取り除く（docs/15 6.57 の P3）。

重なった面は、遠くでちらつき（Z ファイティング）の原因になる。同じ平面（向きと位置を丸めて同じ）の三角形どうしで、小さい三角形の
重心が大きい三角形の中にあれば、小さいほうを外す。位置・UV・面の向きは変えない（面の行だけ書き直す）。

使い方: python3 -I tools/assets/plateau_cleanup.py 入力.obj 出力.obj
"""
import collections
import sys

import numpy as np


def main():
    source, target = sys.argv[1], sys.argv[2]
    lines = open(source, encoding='utf-8').read().split('\n')
    positions = []
    faces = []  # (行の番号, [(v, 残りの文字), ...])
    for number, line in enumerate(lines):
        if line.startswith('v '):
            positions.append([float(t) for t in line.split()[1:4]])
        elif line.startswith('f '):
            corners = []
            for token in line.split()[1:]:
                head, _, rest = token.partition('/')
                corners.append((int(head) - 1, rest))
            faces.append((number, corners))
    V = np.array(positions)
    triangles = []  # (面の番号, 角 3 つ)
    for face_index, (_, corners) in enumerate(faces):
        for k in range(1, len(corners) - 1):
            triangles.append((face_index, (corners[0], corners[k], corners[k + 1])))
    idx = np.array([[c[0][0], c[1][0], c[2][0]] for _, c in triangles])
    a, b, c = V[idx[:, 0]], V[idx[:, 1]], V[idx[:, 2]]
    cross = np.cross(b - a, c - a)
    area = np.linalg.norm(cross, axis=1) * 0.5
    good = area > 1e-6
    normal = np.zeros_like(cross)
    normal[good] = cross[good] / (2.0 * area[good])[:, None]
    offset = (normal * a).sum(1)
    centroid = (a + b + c) / 3.0
    groups = collections.defaultdict(list)
    for i in np.where(good)[0]:
        groups[(tuple(np.round(normal[i] * 40).astype(int)), int(round(offset[i] / 0.08)))].append(i)

    def inside(point, t):
        p0, p1, p2 = V[idx[t, 0]], V[idx[t, 1]], V[idx[t, 2]]
        v0, v1, v2 = p2 - p0, p1 - p0, point - p0
        d00, d01, d02, d11, d12 = v0 @ v0, v0 @ v1, v0 @ v2, v1 @ v1, v1 @ v2
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-12:
            return False
        u = (d11 * d02 - d01 * d12) / den
        w = (d00 * d12 - d01 * d02) / den
        return u > 0.02 and w > 0.02 and u + w < 0.98

    drop = set(int(i) for i in np.where(~good)[0])
    for members in groups.values():
        if len(members) < 2 or len(members) > 400:
            continue
        for i in members:
            for j in members:
                if i != j and area[i] <= area[j] and inside(centroid[i], j):
                    drop.add(int(i))
                    break
    # 面ごとに残す三角形を書き直す（三角形に分けた面の行で置き換える）。
    kept = collections.defaultdict(list)
    for t, (face_index, corners) in enumerate(triangles):
        if t not in drop:
            kept[face_index].append(corners)
    out = []
    face_lines = {number: face_index for face_index, (number, _) in enumerate(faces)}
    for number, line in enumerate(lines):
        if number in face_lines:
            for corners in kept.get(face_lines[number], []):
                out.append('f ' + ' '.join(f'{v + 1}/{rest}' if rest else f'{v + 1}' for v, rest in corners))
        else:
            out.append(line)
    open(target, 'w', encoding='utf-8').write('\n'.join(out))
    print(f'{target}: 三角形 {len(triangles)} → {len(triangles) - len(drop)}（つぶれた三角形と重なった三角形 {len(drop)} を外した）')


if __name__ == '__main__':
    main()
