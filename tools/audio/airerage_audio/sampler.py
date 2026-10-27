"""録音された音源（サンプル）を鳴らす（ドラム・ベース・ローズ）。

SFZ の一部だけを読む（<region> の sample・lokey・hikey・key・pitch_keycenter・lovel・hivel・seq_position、
<group>/<master>/<global> の継承、#include、default_path）。足りない機能（クロスフェード、CC など）は使わない。
"""

import glob
import os
import re
from collections import defaultdict
from dataclasses import dataclass, field
from typing import Dict, List, Optional

import numpy as np
import soundfile as sf
import soxr

from . import SAMPLE_RATE
from .notation import Note

SAMPLES_DIR = os.environ.get("AIRERAGE_AUDIO_DIR", "/opt/airerage-audio") + "/samples"

_cache: Dict[tuple, np.ndarray] = {}


def load_sample(path: str, semitones: float = 0.0) -> np.ndarray:
    """音源を読み、48kHz のステレオ（n, 2）にする。semitones だけ高さを変える（速さも変わる）。"""
    key = (path, round(semitones, 4))
    if key in _cache:
        return _cache[key]
    data, rate = sf.read(path, dtype="float32", always_2d=True)
    if data.shape[1] == 1:
        data = np.repeat(data, 2, axis=1)
    ratio = 2.0 ** (semitones / 12.0)
    source_rate = rate * ratio
    if abs(source_rate - SAMPLE_RATE) > 1e-6:
        data = soxr.resample(data, source_rate, SAMPLE_RATE, quality="VHQ").astype(np.float32)
    _cache[key] = data
    return data


@dataclass
class Region:
    samples: List[str]  # 交互に鳴らす音（ラウンドロビン）
    lokey: int
    hikey: int
    center: int
    lovel: int = 1
    hivel: int = 127


_NOTE_RE = re.compile(r"^([a-gA-G])([#b]?)(-?\d+)$")


def _key(text: str) -> int:
    text = text.strip()
    if text.lstrip("-").isdigit():
        return int(text)
    m = _NOTE_RE.match(text)
    if not m:
        raise ValueError(f"SFZ の音名が読めない: {text}")
    base = {"c": 0, "d": 2, "e": 4, "f": 5, "g": 7, "a": 9, "b": 11}[m.group(1).lower()]
    base += {"#": 1, "b": -1, "": 0}[m.group(2)]
    return 12 * (int(m.group(3)) + 1) + base


def _read_sfz_text(path: str) -> str:
    """#include を展開した SFZ の本文（コメントを除く）。"""
    base = os.path.dirname(path)
    out = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.split("//")[0]
            m = re.match(r'\s*#include\s+"([^"]+)"', line)
            if m:
                out.append(_read_sfz_text(os.path.join(base, m.group(1).replace("\\", "/"))))
            else:
                out.append(line)
    return "\n".join(out)


def parse_sfz(path: str, key_offset: int = 0, sample_root: Optional[str] = None,
              lovel: Optional[int] = None, hivel: Optional[int] = None) -> List[Region]:
    """SFZ を読んで音域ごとの領域にする。key_offset は鍵盤と実際の高さのずれ（半音）。
    lovel・hivel を渡すと、強さの範囲をその値にする（強さごとに分かれたファイルを読むとき）。離したときの音は読まない。"""
    text = _read_sfz_text(path)
    root = sample_root or os.path.dirname(path)
    headers = {"global": {}, "master": {}, "group": {}}
    current = None
    default_path = ""
    raw = []
    for m in re.finditer(r"<(\w+)>|([A-Za-z_0-9]+)=((?:(?!\s+[A-Za-z_0-9]+=|<).)*)", text, re.S):
        if m.group(1):
            name = m.group(1)
            if name == "region":
                current = dict(headers["global"], **headers["master"], **headers["group"])
                raw.append(current)
            elif name in headers:
                if name == "global":
                    headers["master"], headers["group"] = {}, {}
                elif name == "master":
                    headers["group"] = {}
                headers[name] = {}
                current = headers[name]
            else:
                current = {}
            continue
        k, v = m.group(2), m.group(3).strip()
        if k == "default_path":
            default_path = v.replace("\\", "/")
            continue
        if current is not None:
            current[k] = v
    grouped: Dict[tuple, Region] = {}
    order = []
    for r in raw:
        if "sample" not in r or r.get("trigger", "attack") != "attack":
            continue
        sample = os.path.normpath(os.path.join(root, default_path, r["sample"].replace("\\", "/")))
        if "key" in r:
            lo = hi = center = _key(r["key"])
        else:
            lo = _key(r.get("lokey", "0"))
            hi = _key(r.get("hikey", "127"))
            center = _key(r.get("pitch_keycenter", r.get("lokey", "60")))
        vlo = lovel if lovel is not None else int(r.get("lovel", 1))
        vhi = hivel if hivel is not None else int(r.get("hivel", 127))
        k = (lo, hi, center, vlo, vhi)
        if k not in grouped:
            grouped[k] = Region([], lo + key_offset, hi + key_offset, center + key_offset, vlo, vhi)
            order.append(k)
        pos = int(r.get("seq_position", 1))
        grouped[k].samples.insert(pos - 1, sample)
    return [grouped[k] for k in order]


def _velocity_gain(vel: int, region: Region, curve: float) -> float:
    """層の中での強さ。層の上限で 1。"""
    return max(0.25, (vel / max(1, region.hivel))) ** curve


class MultiSampler:
    """音域と強さで音源を選んで鳴らす楽器（ベース・ローズ）。"""

    def __init__(self, regions: List[Region], release: float = 0.25, velocity_curve: float = 1.2,
                 max_shift: float = 7.0):
        self.regions = regions
        self.release = release
        self.velocity_curve = velocity_curve
        self.max_shift = max_shift
        self._rr = defaultdict(int)

    def _region(self, note: Note) -> Region:
        fitting = [r for r in self.regions if r.lokey <= note.pitch <= r.hikey and r.lovel <= note.vel <= r.hivel]
        if not fitting:
            fitting = [r for r in self.regions if r.lovel <= note.vel <= r.hivel] or self.regions
            fitting = sorted(fitting, key=lambda r: abs(r.center - note.pitch))[:1]
        # 強さの範囲が重なるとき（ローズ）は、範囲の真ん中が近い層にする。
        return min(fitting, key=lambda r: abs(note.vel - (r.lovel + r.hivel) / 2))

    def render(self, notes: List[Note], bpm: float, seconds: float, sample_select=None) -> np.ndarray:
        out = np.zeros((int(seconds * SAMPLE_RATE) + 1, 2), dtype=np.float32)
        beat = 60.0 / bpm
        for note in sorted(notes, key=lambda n: n.start):
            region = sample_select(note) if sample_select else self._region(note)
            shift = note.pitch - region.center
            if abs(shift) > self.max_shift:
                raise ValueError(f"音源の高さから {shift} 半音も離れている: {note}")
            key = (id(region), note.pitch)
            path = region.samples[self._rr[key] % len(region.samples)]
            self._rr[key] += 1
            data = load_sample(path, shift)
            gain = _velocity_gain(note.vel, region, self.velocity_curve) * note.extra.get("gain", 1.0)
            start = int((note.start * beat + note.extra.get("offset", 0.0)) * SAMPLE_RATE)
            hold = int(note.dur * beat * SAMPLE_RATE)
            rel = int(note.extra.get("release", self.release) * SAMPLE_RATE)
            length = min(len(data), hold + rel)
            env = np.ones(length, dtype=np.float32)
            if hold < length:
                tail = length - hold
                env[hold:] = np.exp(-np.linspace(0, 6.0, tail)).astype(np.float32)
            skip = max(0, -start)  # 揺らぎで曲の頭より前になった分は切る
            start += skip
            end = min(len(out), start + length - skip)
            if start >= len(out) or end <= start:
                continue
            out[start:end] += data[skip:skip + end - start] * (env[skip:skip + end - start, None] * gain)
        return out


# ---- ドラム（Virtuosity Drums。ファイル名で選ぶ） ----

@dataclass
class DrumPiece:
    folder: str
    articulation: str
    mics: Dict[str, float]
    choke: Optional[str] = None
    layers: List[List[str]] = field(default_factory=list)  # [マイク][強さ][交互]


DEFAULT_MICS = {
    "kick": {"kickmic": 1.0, "oh": 0.45, "room": 0.35},
    "snare": {"snaremic": 1.0, "oh": 0.55, "room": 0.35},
    "cymbal": {"oh": 1.0, "room": 0.3},
    "tom": {"oh": 1.0, "mid": 0.6, "room": 0.35},
}

DRUM_PIECES = {
    "kick": ("kick", "kick_snon", "kick", None),
    "snare": ("snare", "snare_center", "snare", None),
    "rim": ("snare", "snare_rimshot", "snare", None),
    "xstick": ("snare", "snare_crossstick", "snare", None),
    "hh": ("hh", "hh_closed", "cymbal", "hh"),
    "hh_half": ("hh", "hh_half", "cymbal", "hh"),
    "hh_open": ("hh", "hh_open", "cymbal", "hh"),
    "hh_pedal": ("hh", "hh_pedal", "cymbal", "hh"),
    "ride": ("ride", "ride_ride", "cymbal", None),
    "bell": ("ride", "ride_bell", "cymbal", None),
    "crash": ("crash", "crash_crash", "cymbal", None),
    "crash2": ("flatride", "flatride_crash", "cymbal", None),
    "splash": ("hh", "hh_splash", "cymbal", None),
    "tom_hi": ("htom", "htom_center", "tom", None),
    "tom_lo": ("ltom", "ltom_center", "tom", None),
}


class DrumKit:
    """部位の名前（kick・snare・hh など）と強さで鳴らす。マイクの混ぜ方は部位ごとに決める。"""

    def __init__(self, root: Optional[str] = None, mics: Optional[Dict[str, Dict[str, float]]] = None):
        self.root = root or os.path.join(SAMPLES_DIR, "virtuosity_drums", "Samples")
        self.mics = dict(DEFAULT_MICS, **(mics or {}))
        self.pieces: Dict[str, DrumPiece] = {}
        self._rr = defaultdict(int)

    def piece(self, name: str) -> DrumPiece:
        if name in self.pieces:
            return self.pieces[name]
        folder, art, mic_kind, choke = DRUM_PIECES[name]
        piece = DrumPiece(folder, art, self.mics[mic_kind], choke)
        for mic in piece.mics:
            files = glob.glob(os.path.join(self.root, mic, folder, f"{mic}_{art}_*.flac"))
            files += glob.glob(os.path.join(self.root, mic, folder, f"{mic}_{art}_*.wav"))
            layers = defaultdict(list)
            for f in files:
                m = re.fullmatch(rf"{mic}_{art}(?:_vl(\d+))?(?:_rr(\d+))?\.\w+", os.path.basename(f))
                if m:  # 強さの段階がない音（スプラッシュ）は 1 段階
                    layers[int(m.group(1) or 1)].append((int(m.group(2) or 1), f))
            if not layers:
                raise FileNotFoundError(f"ドラムの音源がない: {mic}/{folder}/{art}")
            piece.layers.append([[f for _, f in sorted(layers[k])] for k in sorted(layers)])
        self.pieces[name] = piece
        return piece

    def render(self, notes: List[Note], bpm: float, seconds: float) -> np.ndarray:
        out = np.zeros((int(seconds * SAMPLE_RATE) + 1, 2), dtype=np.float32)
        beat = 60.0 / bpm
        sounding: Dict[str, List[tuple]] = defaultdict(list)  # 止める組 → (始まり, 長さ, 音)
        for note in sorted(notes, key=lambda n: n.start):
            piece = self.piece(note.art)
            start = int((note.start * beat + note.extra.get("offset", 0.0)) * SAMPLE_RATE)
            if start >= len(out):
                continue
            if piece.choke:
                # 同じ組の前の音（開いたハイハット）を、30ms で止める。
                for s, length, mixed in sounding[piece.choke]:
                    cut = start - s
                    if 0 < cut < length:
                        fade = min(int(0.03 * SAMPLE_RATE), length - cut)
                        env = np.zeros(length - cut, dtype=np.float32)
                        env[:fade] = np.linspace(1, 0, fade)
                        seg_end = min(len(out), s + length)
                        n = seg_end - (s + cut)
                        if n > 0:
                            out[s + cut:seg_end] -= mixed[cut:cut + n] * (1 - env[:n, None])
                sounding[piece.choke] = []
            mixed = None
            for (mic, gain), layers in zip(piece.mics.items(), piece.layers):
                index = min(len(layers) - 1, int(note.vel / 128 * len(layers)))
                rr_key = (note.art, mic, index)
                files = layers[index]
                data = load_sample(files[self._rr[rr_key] % len(files)])
                self._rr[rr_key] += 1
                layer_top = (index + 1) / len(layers) * 127
                g = gain * min(1.0, note.vel / layer_top) ** 0.8 * note.extra.get("gain", 1.0)
                if mixed is None:
                    mixed = data * g
                else:
                    n = max(len(mixed), len(data))
                    tmp = np.zeros((n, 2), dtype=np.float32)
                    tmp[: len(mixed)] += mixed
                    tmp[: len(data)] += data * g
                    mixed = tmp
            if start < 0:
                mixed = mixed[-start:]
                start = 0
            end = min(len(out), start + len(mixed))
            out[start:end] += mixed[: end - start]
            if piece.choke:
                sounding[piece.choke].append((start, len(mixed), mixed))
        return out


def black_bass() -> MultiSampler:
    """Black And Blue Basses の黒いほう（指弾き、ネックのピックアップ）。音源は名前より 1 オクターブ低く鳴る。"""
    programs = os.path.join(SAMPLES_DIR, "black_and_blue_basses", "Programs")
    regions = []
    for layer, lo, hi in (("p", 1, 31), ("mp", 32, 63), ("mf", 64, 95), ("f", 96, 127)):
        regions += parse_sfz(os.path.join(programs, "maps", f"darkblack_reg_{layer}_map.sfz"), key_offset=-12,
                             sample_root=programs, lovel=lo, hivel=hi)
    return MultiSampler(regions, release=0.08, velocity_curve=1.0, max_shift=2)


def black_bass_ghost() -> MultiSampler:
    """同じベースのゴーストノート（弦をミュートした音）。"""
    programs = os.path.join(SAMPLES_DIR, "black_and_blue_basses", "Programs")
    regions = []
    for layer, lo, hi in (("p", 1, 63), ("f", 64, 127)):
        regions += parse_sfz(os.path.join(programs, "maps", f"darkblack_ghost_{layer}_map.sfz"), key_offset=-12,
                             sample_root=programs, lovel=lo, hivel=hi)
    return MultiSampler(regions, release=0.03, velocity_curve=1.0, max_shift=12)


def rhodes() -> MultiSampler:
    """jRhodes3d（1977 年のローズ。ステレオのコーラス入り）。"""
    path = os.path.join(SAMPLES_DIR, "jRhodes3d", "jRhodes3d-st.sfz")
    return MultiSampler(parse_sfz(path), release=0.45, velocity_curve=0.9, max_shift=4)
