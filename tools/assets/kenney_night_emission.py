#!/usr/bin/env python3
"""Kenney の City Kit の色の絵（colormap.png）から、夜に窓だけを光らせる発光の絵を作る（docs/15 6.57）。

色の絵の窓の色（青と水色の 2 つのセル。上から 2 段目の左から 5〜6 番目）だけを、暖かい明かりと白っぽい明かりにして、ほかは黒にする。
材質（`Editor/Stages/ThirdPartyProps`）は、この絵を発光の絵（_EmissionMap）に使う。

使い方: python3 -I tools/assets/kenney_night_emission.py 入力の colormap.png 出力.png
"""
import sys

import numpy as np
from PIL import Image


def main():
    source, target = sys.argv[1], sys.argv[2]
    image = np.asarray(Image.open(source).convert('RGB'), dtype=np.float32) / 255.0
    height, width, _ = image.shape
    cell_w, cell_h = width // 8, height // 4
    out = np.zeros_like(image)
    # 窓のセル（青と水色）。左の半分と右の半分で色を変え、セルの中の明るさの変化（グラデーション）を残す。
    warm = np.array([1.0, 0.78, 0.45], dtype=np.float32)
    cool = np.array([0.72, 0.88, 1.0], dtype=np.float32)
    for column, color in ((4, warm), (5, cool)):
        x0, y0 = column * cell_w, cell_h
        cell = image[y0:y0 + cell_h, x0:x0 + cell_w]
        brightness = cell.mean(axis=2, keepdims=True)
        out[y0:y0 + cell_h, x0:x0 + cell_w] = color * (0.55 + 0.45 * brightness)
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(target)
    print(f'{target}: {width}x{height}')


if __name__ == '__main__':
    main()
