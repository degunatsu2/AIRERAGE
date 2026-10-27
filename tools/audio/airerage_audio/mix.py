"""音の加工・混ぜ合わせ・仕上げ（マスタリング）・書き出し。"""

import os
import subprocess
import tempfile
from typing import Iterable, List, Optional, Sequence

import numpy as np
import pedalboard as pb
import pyloudnorm
import soundfile as sf
import soxr

from . import SAMPLE_RATE

HALL = "/usr/lib/vst3/DragonflyHallReverb.vst3"
PLATE = "/usr/lib/vst3/DragonflyPlateReverb.vst3"
ROOM = "/usr/lib/vst3/DragonflyRoomReverb.vst3"

_meter = pyloudnorm.Meter(SAMPLE_RATE)


def db(value: float) -> float:
    return 10.0 ** (value / 20.0)


def fit(a: np.ndarray, length: int) -> np.ndarray:
    """長さを length にそろえる（足りなければ無音を足す）。"""
    if len(a) >= length:
        return a[:length]
    return np.concatenate([a, np.zeros((length - len(a), a.shape[1]), dtype=a.dtype)])


def pan(a: np.ndarray, position: float) -> np.ndarray:
    """左右の位置（-1 が左、1 が右）。音量の合計は変えない。"""
    angle = (position + 1) * np.pi / 4
    return np.stack([a[:, 0] * np.cos(angle) * np.sqrt(2), a[:, 1] * np.sin(angle) * np.sqrt(2)], axis=1)


def width(a: np.ndarray, amount: float) -> np.ndarray:
    """広がり（0 でモノラル、1 でそのまま、1 より大きいと広く）。"""
    mid = (a[:, 0] + a[:, 1]) / 2
    side = (a[:, 0] - a[:, 1]) / 2 * amount
    return np.stack([mid + side, mid - side], axis=1)


def process(a: np.ndarray, effects: Sequence) -> np.ndarray:
    """pedalboard のエフェクトの並びを通す。"""
    if not effects:
        return a
    board = pb.Pedalboard(list(effects))
    return board(a.T.astype(np.float32), SAMPLE_RATE).T


def reverb(a: np.ndarray, plugin: str = HALL, **params) -> np.ndarray:
    """Dragonfly の残響だけを返す（センドで使う）。"""
    effect = pb.load_plugin(plugin)
    effect.dry_level = 0.0
    for key, value in params.items():
        setattr(effect, key, value)
    return effect(a.T.astype(np.float32), SAMPLE_RATE).T


def loudness(a: np.ndarray) -> float:
    """統合ラウンドネス（LUFS）。"""
    if np.abs(a).max() < 1e-7:
        return -120.0
    return float(_meter.integrated_loudness(a.astype(np.float64)))


def to_loudness(a: np.ndarray, target: float) -> np.ndarray:
    current = loudness(a)
    if current < -100:
        return a
    return a * db(target - current)


def true_peak(a: np.ndarray) -> float:
    """4 倍に補間したときの最大値（dBTP）。"""
    up = soxr.resample(a.astype(np.float64), SAMPLE_RATE, SAMPLE_RATE * 4)
    return 20 * np.log10(np.abs(up).max() + 1e-12)


def duck(a: np.ndarray, hits: Iterable[float], depth_db: float = 3.0, attack: float = 0.005,
         release: float = 0.18) -> np.ndarray:
    """キックの時刻（秒）で少しだけ下げる（サイドチェイン）。"""
    gain = np.ones(len(a), dtype=np.float32)
    floor = db(-depth_db)
    a_len = int(attack * SAMPLE_RATE)
    r_len = int(release * SAMPLE_RATE)
    shape = np.concatenate([np.linspace(1, floor, max(1, a_len)), floor + (1 - floor) * (1 - np.exp(-np.linspace(0, 5, r_len)))])
    for t in hits:
        s = int(t * SAMPLE_RATE)
        e = min(len(a), s + len(shape))
        if s < len(a):
            gain[s:e] = np.minimum(gain[s:e], shape[: e - s])
    return a * gain[:, None]


def fade(a: np.ndarray, fade_in: float = 0.0, fade_out: float = 0.0) -> np.ndarray:
    out = a.copy()
    if fade_in > 0:
        n = int(fade_in * SAMPLE_RATE)
        out[:n] *= np.linspace(0, 1, n)[:, None]
    if fade_out > 0:
        n = int(fade_out * SAMPLE_RATE)
        out[-n:] *= (np.cos(np.linspace(0, np.pi / 2, n)) ** 2)[:, None]
    return out


def master(a: np.ndarray, target_lufs: float = -16.0, ceiling_db: float = -1.0) -> np.ndarray:
    """仕上げ: 軽いコンプレッサーでまとめ、リミッターで頭を抑え、ラウドネスを target_lufs にそろえる。
    pedalboard の Limiter は出力を持ち上げるので、通した後に測って下げる（下げる分には頭は超えない）。"""
    glued = process(a, [
        pb.HighpassFilter(cutoff_frequency_hz=25),
        pb.Compressor(threshold_db=-14, ratio=1.8, attack_ms=25, release_ms=220),
    ])
    gain = target_lufs - loudness(glued)
    out = glued
    for _ in range(6):
        out = process(glued * db(gain), [pb.Limiter(threshold_db=-3.0, release_ms=120)])
        diff = target_lufs - loudness(out)
        if diff <= 0.1:
            out = out * db(min(0.0, diff))
            break
        gain += diff
    peak = true_peak(out)
    if peak > ceiling_db:
        out = out * db(ceiling_db - peak)
    return out.astype(np.float32)


def bands(a: np.ndarray) -> str:
    """オクターブごとの平均の強さ（dB。全体の釣り合いを確かめる）。"""
    mono = a.mean(axis=1)
    spec = np.abs(np.fft.rfft(mono[: 2 ** int(np.log2(len(mono)))])) ** 2
    freqs = np.fft.rfftfreq(2 ** int(np.log2(len(mono))), 1 / SAMPLE_RATE)
    out = []
    for center in (31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000):
        sel = (freqs >= center / np.sqrt(2)) & (freqs < center * np.sqrt(2))
        out.append(f"{center}:{10 * np.log10(spec[sel].sum() + 1e-12):.0f}")
    return " ".join(out)


def write(path: str, a: np.ndarray) -> None:
    """WAV（24 ビット）か OGG（Vorbis、品質 6）で書く。OGG は ffmpeg で作る（libsndfile は長い曲で落ちる）。"""
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    if os.path.splitext(path)[1].lower() != ".ogg":
        sf.write(path, a, SAMPLE_RATE, subtype="PCM_24")
        return
    with tempfile.TemporaryDirectory() as tmp:
        wav = os.path.join(tmp, "source.wav")
        sf.write(wav, a, SAMPLE_RATE, subtype="FLOAT")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "6", path],
                       check=True)


def summary(name: str, a: np.ndarray) -> str:
    return f"{name}: {len(a) / SAMPLE_RATE:.1f} 秒、{loudness(a):.1f} LUFS、真のピーク {true_peak(a):.1f} dBTP"
