"""メニューの決定音（タイトルの決定・キャラクターの決定）。

案 2（2026-10-27）: ユーザーが選んだ方向（タイトルは「透明感のある広がり」、キャラクターは「上がる 2 音のチャイム」）で、
「音が厚すぎて効果音に向かない」という指摘から、重ねる音を 1〜2 種類に減らし、余韻を短くした。押してすぐ鳴る（遅れ 0.05 秒以内）。
"""

from typing import Callable, Dict

import numpy as np

from airerage_audio import mix
from airerage_audio.sfx import boom, finish, noise_sweep, place, silence, synth


def _room(a: np.ndarray, amount: float = 0.2) -> np.ndarray:
    """短い残響を少しだけ。"""
    wet = mix.reverb(a, decay_s=1.0, size_m=18, predelay_ms=8, early_level=10, late_level=100, high_cut_hz=10000,
                     low_cut_hz=200, width=120)
    return a + wet * amount


def title_b1() -> np.ndarray:
    """B1: エレピの和音を素早くかき鳴らし、上にベルを 1 音。"""
    out = silence(1.6)
    strum = [(i * 0.018, 0.9, p, 106) for i, p in enumerate((64, 71, 75, 78))]  # E4 B4 D#5 F#5
    place(out, mix.to_loudness(synth("Keys/DX EP", strum, 1.6), -20), 0.0)
    place(out, mix.to_loudness(synth("Plucks/Bell 2", [(0.07, 0.5, 88, 110)], 1.6), -24), 0.0)  # E6
    return finish(_room(out))


def title_b2() -> np.ndarray:
    """B2: ベルの速い上りの 5 音（最後の音を伸ばす）。"""
    out = silence(1.6)
    notes = [(i * 0.035, 0.12 if i < 4 else 0.7, p, 100 + i * 4) for i, p in enumerate((83, 88, 90, 92, 95))]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", notes, 1.6), -20), 0.0)
    return finish(_room(out, 0.25))


def title_b3() -> np.ndarray:
    """B3: エレピの 2 音（E と B）とベルの 2 音を同時に。いちばん短い。"""
    out = silence(1.3)
    place(out, mix.to_loudness(synth("Keys/DX EP", [(0.0, 0.6, 76, 110), (0.0, 0.6, 83, 106)], 1.3), -20), 0.0)
    place(out, mix.to_loudness(synth("Plucks/Bell 2", [(0.0, 0.4, 88, 104), (0.0, 0.4, 95, 96)], 1.3), -25), 0.0)
    return finish(_room(out, 0.15))


def character_c1() -> np.ndarray:
    """C1: 上がる 2 音のチャイムだけ（B5 → E6）。"""
    out = silence(1.2)
    chime = [(0.0, 0.08, 83, 108), (0.07, 0.5, 88, 118)]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", chime, 1.2), -20), 0.0)
    return finish(_room(out))


def character_c2() -> np.ndarray:
    """C2: ごく短い風切り（0.12 秒）に重ねて、2 音のチャイム。"""
    out = silence(1.2)
    place(out, noise_sweep(0.14, 6000, 1500, 0.8, 0.003, 0.06, seed=4), 0.0, -22)
    chime = [(0.03, 0.08, 83, 108), (0.10, 0.5, 88, 118)]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", chime, 1.2), -20), 0.0)
    return finish(_room(out))


def character_c3() -> np.ndarray:
    """C3: 2 音のチャイム（ベルとエレピを重ねる）と、軽い低いドン。"""
    out = silence(1.2)
    chime = [(0.0, 0.08, 83, 108), (0.07, 0.5, 88, 118)]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", chime, 1.2), -21), 0.0)
    place(out, mix.to_loudness(synth("Keys/DX EP", chime, 1.2), -25), 0.0)
    place(out, boom(0.25, 110, 55, 14.0), 0.07, -16)
    return finish(_room(out))


SOUNDS: Dict[str, Callable[[], np.ndarray]] = {
    "se_title_start_b1": title_b1,
    "se_title_start_b2": title_b2,
    "se_title_start_b3": title_b3,
    "se_character_decide_c1": character_c1,
    "se_character_decide_c2": character_c2,
    "se_character_decide_c3": character_c3,
}
