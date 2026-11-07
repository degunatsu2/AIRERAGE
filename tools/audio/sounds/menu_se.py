"""メニューの決定音（タイトルの決定・キャラクターの決定）。

案 2（2026-10-27）: ユーザーが選んだ方向（タイトルは「透明感のある広がり」、キャラクターは「上がる 2 音のチャイム」）で、
「音が厚すぎて効果音に向かない」という指摘から、重ねる音を 1〜2 種類に減らし、余韻を短くした。押してすぐ鳴る（遅れ 0.05 秒以内）。
"""

from typing import Callable, Dict

import numpy as np

import scipy.signal as ss

from airerage_audio import SAMPLE_RATE, mix
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


# ---- 2026-11-07: メニューの演出（D-87・D-88）に合わせた音 ----------------------------------------------------------------
# ユーザーの要望: カーソルを合わせたときは「ダイヤルを回したときのようなカチッという音（あまり主張させない）」、タイトルの決定・
# ボタンの決定・キャラクターの決定は、新しい演出に合わせた瀟洒な音に。ゲームに入れるのは下の 4 つ（se_ui_cursor など）。


def ui_cursor() -> np.ndarray:
    """カーソル: ダイヤルの爪が溝に落ちる短いカチッ（0.06 秒）。鋭いクリックと小さな金属の鳴り、8ms 後に小さな跳ね返り。"""
    sr = SAMPLE_RATE
    n = int(0.06 * sr)
    t = np.arange(n) / sr
    rng = np.random.default_rng(7)
    b, a = ss.butter(2, [2500 / (sr / 2), 9000 / (sr / 2)], "band")
    click = ss.lfilter(b, a, rng.standard_normal(n)) * np.exp(-t / 0.0035) * np.minimum(1.0, t / 0.0003)
    click /= np.abs(click).max() + 1e-9
    ping = (np.sin(2 * np.pi * 3400 * t) * 0.6 + np.sin(2 * np.pi * 5150 * t) * 0.3) * np.exp(-t / 0.006)
    body = np.sin(2 * np.pi * 900 * t) * np.exp(-t / 0.005)
    x = click + ping * 0.35 + body * 0.3
    d = int(0.008 * sr)
    x[d:] += click[: n - d] * 0.2 + ping[: n - d] * 0.07
    x *= np.minimum(1.0, t / 0.0002)
    stereo = np.stack([x, x], axis=1).astype(np.float32)
    return finish(stereo, peak_db=-9.0, fade_out=0.01)


def ui_confirm() -> np.ndarray:
    """ボタンの決定: 柔らかい押し込みの低いコツと、ガラスのような 2 音（E6・B6）、少し遅れて高いきらめき 1 音。短い余韻。"""
    out = silence(0.9)
    place(out, boom(0.09, 420, 180, 40.0), 0.0, -20)
    place(out, mix.to_loudness(synth("Plucks/Bell 2", [(0.0, 0.22, 88, 100), (0.0, 0.22, 95, 92)], 0.9), -21), 0.0)
    place(out, mix.to_loudness(synth("Plucks/Diamonds", [(0.04, 0.18, 100, 88)], 0.9), -29), 0.0)
    return finish(_room(out, 0.12), peak_db=-3.0)


def title_start() -> np.ndarray:
    """タイトルの決定: 空へ吸い込まれる風（0.42 秒）が強まり、白い閃光の瞬間（0.36 秒）に Emaj9 の和音を軽くかき鳴らし、高いきらめきが上る。"""
    out = silence(2.0)
    # 押した瞬間に小さな高い 1 音（すぐ鳴ったと分かるように）。風はそれに続いて強まる。
    place(out, mix.to_loudness(synth("Plucks/Diamonds", [(0.0, 0.1, 95, 70)], 2.0), -32), 0.0)
    place(out, noise_sweep(0.42, 500, 7000, 1.0, 0.15, 0.04, seed=11), 0.0, -24)
    chord = [(0.36 + i * 0.012, 1.1, p, 96 + i * 3) for i, p in enumerate((64, 71, 75, 78, 83))]  # E4 B4 D#5 F#5 B5
    place(out, mix.to_loudness(synth("Keys/Soft Suitcase", chord, 2.0), -22), 0.0)
    sparkle = [(0.36 + i * 0.03, 0.4, p, 90) for i, p in enumerate((88, 92, 95, 99))]  # E6 G#6 B6 D#7
    place(out, mix.to_loudness(synth("Plucks/Diamonds", sparkle, 2.0), -27), 0.0)
    return finish(_room(out, 0.3))


def character_decide() -> np.ndarray:
    """キャラクターの決定: 札が空を切る小さな風に、上がる 3 音のベル（B5・E6・G#6）と、下で支えるエレピの 2 音（E5・G#5）。"""
    out = silence(1.4)
    place(out, noise_sweep(0.12, 7000, 2500, 0.7, 0.002, 0.08, seed=5), 0.0, -26)
    chime = [(0.0, 0.1, 83, 104), (0.06, 0.1, 88, 110), (0.12, 0.6, 92, 116)]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", chime, 1.4), -20), 0.0)
    place(out, mix.to_loudness(synth("Keys/Soft Suitcase", [(0.12, 0.7, 76, 90), (0.12, 0.7, 80, 86)], 1.4), -26), 0.0)
    return finish(_room(out, 0.22))


SOUNDS: Dict[str, Callable[[], np.ndarray]] = {
    "se_title_start_b1": title_b1,
    "se_title_start_b2": title_b2,
    "se_title_start_b3": title_b3,
    "se_character_decide_c1": character_c1,
    "se_character_decide_c2": character_c2,
    "se_character_decide_c3": character_c3,
    "se_ui_cursor": ui_cursor,
    "se_ui_confirm": ui_confirm,
    "se_title_start": title_start,
    "se_character_decide": character_decide,
}
