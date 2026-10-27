"""曲（楽器ごとの音の並びと混ぜ方）を書き出す。

ゲームでくり返す曲は「前奏（1 回だけ）」と「本体（くり返す）」に分ける。本体を 2 回続けて鳴らし、2 回目を切り出すと、
1 回目の終わりの残響が頭に入った「つなぎ目のないループ」になる。
"""

import hashlib
import os
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Sequence

import numpy as np
import pedalboard as pb

from . import SAMPLE_RATE, mix
from .notation import Note
from .sampler import DrumKit, black_bass, black_bass_ghost, rhodes
from .synth import Synth

TAIL = 6.0  # 最後の音の余韻（秒）


@dataclass
class Track:
    name: str
    instrument: str  # drums・bass・rhodes・surge:<音色>
    notes: List[Note]
    loudness: float  # 楽器ごとのラウドネス（LUFS。混ぜる前の釣り合い）
    effects: Sequence = ()
    pan: float = 0.0
    width: float = 1.0
    hall: float = 0.0  # 残響へ送る量（0〜1）
    delay: float = 0.0  # 付点 8 分のディレイへ送る量（0〜1）
    duck: float = 0.0  # キックで下げる量（dB）
    vibrato: float = 0.0  # 長い音のビブラート（半音。シンセの単音の楽器だけ）
    params: Dict[str, float] = field(default_factory=dict)


@dataclass
class Song:
    name: str
    bpm: float
    intro: float  # 前奏の長さ（拍）
    body: float  # 本体の長さ（拍）
    tracks: List[Track]
    transpose: int = 0
    hall: Dict[str, float] = field(default_factory=lambda: dict(
        decay_s=2.4, size_m=32, predelay_ms=24, early_level=12, late_level=100, high_cut_hz=9000, low_cut_hz=120,
        width=120))
    master_lufs: float = -16.0
    # 仕上げの前の音色の調整: 中低域を少し減らし、高域（空気感）を足して、透明感を出す。
    master_eq: Sequence = field(default_factory=lambda: [
        pb.PeakFilter(cutoff_frequency_hz=320, gain_db=-1.5, q=0.7),
        pb.HighShelfFilter(cutoff_frequency_hz=9000, gain_db=2.5)])

    def seconds(self, beats: float) -> float:
        return beats * 60.0 / self.bpm


def _instrument_render(track: Track, notes: List[Note], song: Song, seconds: float) -> np.ndarray:
    kind = track.instrument
    if kind == "drums":
        return DrumKit().render(notes, song.bpm, seconds)
    if kind == "bass":
        main = [n for n in notes if n.art != "ghost"]
        ghost = [n for n in notes if n.art == "ghost"]
        a = black_bass().render(main, song.bpm, seconds)
        if ghost:
            a = a + black_bass_ghost().render(ghost, song.bpm, seconds) * 0.7
        return a
    if kind == "rhodes":
        return rhodes().render(notes, song.bpm, seconds)
    if kind.startswith("surge:"):
        return Synth(kind[len("surge:"):], params=track.params).render(notes, song.bpm, seconds, track.vibrato)
    raise ValueError(f"楽器が分からない: {kind}")


def _cached_render(track: Track, notes: List[Note], song: Song, seconds: float, out_dir: str) -> np.ndarray:
    """楽器の音を書き出す。音の並びと楽器が前と同じなら、前に書き出したもの（out/cache）を使う。"""
    key = hashlib.sha256(repr((track.instrument, sorted(track.params.items()), track.vibrato, song.bpm, seconds,
                               [(n.start, n.dur, n.pitch, n.vel, n.art, sorted(n.extra.items())) for n in notes]))
                         .encode()).hexdigest()[:20]
    path = os.path.join(out_dir, "cache", f"{song.name}_{track.name}_{key}.npy")
    if os.path.exists(path):
        return np.load(path)
    a = _instrument_render(track, notes, song, seconds)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    np.save(path, a)
    return a


def _looped_notes(song: Song, notes: List[Note], passes: int, pitched: bool) -> List[Note]:
    out = [n for n in notes if n.start < song.intro]
    body = [n for n in notes if n.start >= song.intro]
    for p in range(passes):
        out += [n.shifted(p * song.body) for n in body]
    if song.transpose and pitched:
        out = [n.shifted(0, song.transpose) for n in out]
    return out


def render(song: Song, out_dir: str, stems: bool = False, preview_beats: Optional[float] = None) -> Dict[str, str]:
    """前奏と本体 2 回を鳴らし、試聴用（前奏＋本体＋少し）と、ゲーム用の前奏・ループを書き出す。"""
    os.makedirs(out_dir, exist_ok=True)
    total_beats = song.intro + 2 * song.body
    seconds = song.seconds(total_beats) + TAIL
    length = int(seconds * SAMPLE_RATE)
    kicks = []
    dry = np.zeros((length, 2), dtype=np.float32)
    hall_send = np.zeros_like(dry)
    delay_send = np.zeros_like(dry)
    for track in song.tracks:
        notes = _looped_notes(song, track.notes, 2, track.instrument != "drums")
        if track.instrument == "drums":
            kicks = [song.seconds(n.start) + n.extra.get("offset", 0.0) for n in notes if n.art == "kick"]
        print(f"  {track.name}: {len(notes)} 音", flush=True)
        a = mix.fit(_cached_render(track, notes, song, seconds, out_dir), length)
        a = mix.process(a, track.effects)
        if track.width != 1.0:
            a = mix.width(a, track.width)
        if track.pan:
            a = mix.pan(a, track.pan)
        a = mix.to_loudness(a, track.loudness)
        if track.duck and kicks:
            a = mix.duck(a, kicks, track.duck)
        if stems:
            mix.write(os.path.join(out_dir, "stems", f"{track.name}.wav"), a)
        dry += a
        hall_send += a * track.hall
        delay_send += a * track.delay
    beat = 60.0 / song.bpm
    delayed = mix.process(delay_send, [
        pb.HighpassFilter(cutoff_frequency_hz=400), pb.LowpassFilter(cutoff_frequency_hz=7000),
        pb.Delay(delay_seconds=beat * 0.75, feedback=0.32, mix=1.0)])
    hall_send += delayed * 0.5
    wet = mix.reverb(hall_send, **song.hall)
    full = dry + delayed + wet
    full = mix.master(mix.process(full, song.master_eq), song.master_lufs)
    intro_n = int(song.seconds(song.intro) * SAMPLE_RATE)
    body_n = int(round(song.seconds(song.body) * SAMPLE_RATE))
    outputs = {}
    preview_end = intro_n + body_n + int(song.seconds(preview_beats if preview_beats is not None else 32) * SAMPLE_RATE)
    preview = mix.fade(full[:preview_end], 0.0, song.seconds(16))
    outputs["preview"] = os.path.join(out_dir, f"{song.name}_preview.wav")
    mix.write(outputs["preview"], preview)
    if intro_n > 0:
        outputs["intro"] = os.path.join(out_dir, f"{song.name}_intro.ogg")
        mix.write(outputs["intro"], full[:intro_n])
    outputs["loop"] = os.path.join(out_dir, f"{song.name}_loop.ogg")
    loop = full[intro_n + body_n: intro_n + 2 * body_n]
    mix.write(outputs["loop"], loop)
    print("  " + mix.summary("試聴用", preview), flush=True)
    print("  " + mix.summary("ループ", loop), flush=True)
    print("  帯域: " + mix.bands(loop), flush=True)
    return outputs
