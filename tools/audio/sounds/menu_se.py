"""メニューの豪華な効果音の案（タイトルの決定・キャラクターの決定。各 3 案。ユーザーの要望、2026-10-27）。"""

from typing import Callable, Dict

import numpy as np
import pedalboard as pb

from airerage_audio import mix
from airerage_audio.sfx import boom, drums, finish, noise_sweep, place, silence, synth

E_ADD9 = [52, 59, 64, 66, 68, 71]  # E3 B3 E4 F#4 G#4 B4
E_MAJ9_HIGH = [64, 68, 71, 75, 78]  # E4 G#4 B4 D#5 F#5


def _hall(a: np.ndarray, amount: float, decay: float = 2.6) -> np.ndarray:
    wet = mix.reverb(a, decay_s=decay, size_m=36, predelay_ms=12, early_level=15, late_level=100, high_cut_hz=10000,
                     low_cut_hz=200, width=130)
    return a + wet * amount


def title_a() -> np.ndarray:
    """A: ファンファーレの一撃（金管と和音シンセ、キラキラ、クラッシュとキック、低いドン）。"""
    length = 3.0
    out = silence(length)
    chord = [(0.0, 0.42, p, 122) for p in E_ADD9]
    place(out, mix.to_loudness(synth("Brass/Toto Brass", chord, length), -20), 0.0)
    place(out, mix.to_loudness(synth("Polysynths/Jupiter-8", chord, length), -24), 0.0)
    sparkle = [(0.03 + i * 0.045, 0.3, p, 100 - i * 4) for i, p in enumerate([76, 80, 83, 88, 92, 95, 100])]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", sparkle, length), -25), 0.0)
    place(out, drums([(0.0, "kick", 127), (0.0, "crash", 124), (0.0, "rim", 110)], length), 0.0, -4)
    place(out, boom(1.0, 90, 42, 5.0), 0.0, -8)
    place(out, noise_sweep(0.35, 9000, 5000, 1.2, 0.002, 0.3, seed=1), 0.0, -24)
    return finish(_hall(out, 0.5))


def title_b() -> np.ndarray:
    """B: 透明感のある広がり（ローズと DX のエレピの分散和音、2 オクターブのベルのグリッサンド、パッド、軽いキック）。"""
    length = 3.5
    out = silence(length)
    strum = [(i * 0.025, 1.6, p, 104) for i, p in enumerate(E_MAJ9_HIGH)]
    place(out, mix.to_loudness(synth("Keys/DX EP", strum, length), -21), 0.0)
    gliss = [(0.02 + i * 0.028, 0.25, p, 92) for i, p in enumerate([76, 78, 80, 83, 85, 87, 88, 90, 92, 95, 97, 99, 100])]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", gliss, length), -23), 0.0)
    place(out, mix.to_loudness(synth("Pads/Pad 3", [(0.0, 1.2, p, 100) for p in E_MAJ9_HIGH], length), -27), 0.0)
    place(out, drums([(0.0, "kick", 110), (0.0, "bell", 112), (0.0, "splash", 100)], length), 0.0, -6)
    place(out, boom(0.8, 70, 40, 6.0), 0.0, -12)
    return finish(_hall(out, 0.7, 3.2))


def title_c() -> np.ndarray:
    """C: シンセの一撃と上昇する風切り（OB-8 の金管、ノイズの上昇、クラッシュ、低いドン）。"""
    length = 3.0
    out = silence(length)
    place(out, noise_sweep(0.45, 400, 9000, 0.7, 0.01, 0.05, seed=2), 0.0, -14)
    chord = [(0.40, 0.5, p, 124) for p in E_ADD9]
    place(out, mix.to_loudness(synth("Brass/OB-8 Jump", chord, length), -19), 0.0)
    place(out, mix.to_loudness(synth("Plucks/The 1980s", [(0.40, 0.6, p + 12, 110) for p in E_ADD9], length), -25), 0.0)
    place(out, drums([(0.40, "kick", 127), (0.40, "crash", 126), (0.40, "snare", 120)], length), 0.0, -4)
    place(out, boom(1.0, 100, 38, 4.5), 0.40, -7)
    return finish(_hall(out, 0.5))


def character_a() -> np.ndarray:
    """A: 衝撃（キックとリムショット、フラットライドのクラッシュ、低いドン、金管の短い一撃、金属のシャッ）。"""
    length = 2.0
    out = silence(length)
    place(out, drums([(0.0, "kick", 127), (0.0, "rim", 124), (0.0, "crash2", 118)], length), 0.0, -3)
    place(out, boom(0.7, 120, 45, 6.5), 0.0, -6)
    place(out, mix.to_loudness(synth("Brass/Toto Brass", [(0.0, 0.18, p, 124) for p in (59, 64, 66, 71, 76)], length), -21), 0.0)
    place(out, noise_sweep(0.25, 7000, 4000, 0.9, 0.001, 0.2, seed=3), 0.0, -20)
    return finish(_hall(out, 0.35, 1.8))


def character_b() -> np.ndarray:
    """B: パワーコード（シンセの金管のオクターブと、シンセギターの 5 度、キックとクラッシュ）。"""
    length = 2.2
    out = silence(length)
    power = [(0.0, 0.5, p, 124) for p in (40, 47, 52, 59, 64)]
    place(out, mix.to_loudness(synth("Brass/JX-10 Double Brass", power, length), -20), 0.0)
    place(out, mix.to_loudness(synth("Leads/Synth Guitar 1", [(0.0, 0.5, p, 120) for p in (52, 59, 64)], length), -22), 0.0)
    place(out, drums([(0.0, "kick", 127), (0.0, "crash", 120), (0.0, "tom_lo", 118)], length), 0.0, -4)
    place(out, boom(0.8, 80, 40, 5.0), 0.0, -8)
    return finish(_hall(out, 0.35, 2.0))


def character_c() -> np.ndarray:
    """C: ロックオン（下がる風切りから、上がる 2 音のチャイム、キックと低いタム）。"""
    length = 2.0
    out = silence(length)
    place(out, noise_sweep(0.3, 7000, 900, 0.8, 0.005, 0.08, seed=4), 0.0, -16)
    chime = [(0.22, 0.25, 83, 112), (0.30, 0.6, 88, 118), (0.30, 0.6, 95, 100)]
    place(out, mix.to_loudness(synth("Plucks/Bell 2", chime, length), -21), 0.0)
    place(out, mix.to_loudness(synth("Keys/DX EP", chime, length), -24), 0.0)
    place(out, drums([(0.30, "kick", 124), (0.30, "tom_lo", 112), (0.30, "splash", 96)], length), 0.0, -5)
    place(out, boom(0.6, 90, 45, 7.0), 0.30, -10)
    return finish(_hall(out, 0.45, 2.2))


SOUNDS: Dict[str, Callable[[], np.ndarray]] = {
    "se_title_start_a": title_a,
    "se_title_start_b": title_b,
    "se_title_start_c": title_c,
    "se_character_decide_a": character_a,
    "se_character_decide_b": character_b,
    "se_character_decide_c": character_c,
}
