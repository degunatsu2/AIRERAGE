"""効果音の部品: 音程の下がる低音（ブーム）、帯域が動くノイズ（風切り・シャッ）、楽器の音の重ね合わせ。"""

from typing import List, Sequence, Tuple

import numpy as np
import scipy.signal as ss

from . import SAMPLE_RATE, mix
from .notation import Note
from .sampler import DrumKit
from .synth import Synth


def silence(seconds: float) -> np.ndarray:
    return np.zeros((int(seconds * SAMPLE_RATE), 2), dtype=np.float32)


def place(out: np.ndarray, a: np.ndarray, at: float, gain_db: float = 0.0) -> np.ndarray:
    """out の at 秒の位置に a を足す。"""
    s = int(at * SAMPLE_RATE)
    e = min(len(out), s + len(a))
    out[s:e] += a[: e - s] * mix.db(gain_db)
    return out


def boom(seconds: float, start_hz: float, end_hz: float, decay: float = 6.0) -> np.ndarray:
    """音程が下がっていく正弦波の低音（ドン）。"""
    t = np.arange(int(seconds * SAMPLE_RATE)) / SAMPLE_RATE
    freq = end_hz + (start_hz - end_hz) * np.exp(-t * 18)
    phase = 2 * np.pi * np.cumsum(freq) / SAMPLE_RATE
    env = np.exp(-t * decay) * np.minimum(1.0, t / 0.002)
    x = (np.sin(phase) * env).astype(np.float32)
    return np.stack([x, x], axis=1)


def noise_sweep(seconds: float, start_hz: float, end_hz: float, width_oct: float = 0.8, attack: float = 0.01,
                release: float = 0.1, seed: int = 0) -> np.ndarray:
    """帯域の中心が start_hz から end_hz へ動くノイズ（ステレオ）。"""
    rng = np.random.default_rng(seed)
    n = int(seconds * SAMPLE_RATE)
    out = []
    for ch in range(2):
        x = rng.standard_normal(n)
        f, t, z = ss.stft(x, SAMPLE_RATE, nperseg=1024)
        centers = start_hz * (end_hz / start_hz) ** np.clip(t / seconds, 0, 1)
        logf = np.log2(np.maximum(f, 1.0))[:, None]
        mask = np.exp(-0.5 * ((logf - np.log2(centers)[None, :]) / (width_oct / 2)) ** 2)
        _, y = ss.istft(z * mask, SAMPLE_RATE, nperseg=1024)
        out.append(y[:n])
    y = np.stack(out, axis=1)
    env = np.ones(n)
    a = max(1, int(attack * SAMPLE_RATE))
    r = max(1, int(release * SAMPLE_RATE))
    env[:a] = np.linspace(0, 1, a)
    env[-r:] *= np.linspace(1, 0, r)
    y = y * env[:, None]
    return (y / (np.abs(y).max() + 1e-9)).astype(np.float32)


def synth(patch: str, notes: Sequence[Tuple[float, float, int, int]], seconds: float, bpm: float = 60.0) -> np.ndarray:
    """(秒, 長さ秒, 音, 強さ) の並びをシンセで鳴らす（bpm 60 なら拍 = 秒）。"""
    return Synth(patch).render([Note(s, d, p, v) for s, d, p, v in notes], bpm, seconds)


def drums(hits: Sequence[Tuple[float, str, int]], seconds: float) -> np.ndarray:
    """(秒, 部位, 強さ) の並びをドラムで鳴らす。"""
    return DrumKit().render([Note(s, 0.25, 0, v, art=piece) for s, piece, v in hits], 60.0, seconds)


def finish(a: np.ndarray, peak_db: float = -1.0, fade_out: float = 0.15) -> np.ndarray:
    """最後の無音を切り、頭の高さをそろえる。"""
    level = np.abs(a).max(axis=1)
    above = np.nonzero(level > mix.db(-70))[0]
    end = min(len(a), (above[-1] if len(above) else len(a)) + int(0.02 * SAMPLE_RATE))
    out = mix.fade(a[:end], 0.0, fade_out)
    return (out * mix.db(peak_db) / (np.abs(out).max() + 1e-9)).astype(np.float32)
