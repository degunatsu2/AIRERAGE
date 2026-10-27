"""編曲の道具: 和音の進行、ドラムのパターン、ベースの型、伴奏のリズム、アルペジオ、揺らぎ。"""

import random
from dataclasses import dataclass
from typing import Dict, List, Optional, Sequence, Tuple

from .notation import Note, SIXTEENTH
from .theory import Chord, chord, voice

BAR = 4.0


@dataclass
class ChordSpan:
    start: float
    dur: float
    chord: Chord


def progression(text: str, start: float = 0.0) -> List[ChordSpan]:
    """'| Emaj9 | % | G#m7 C#m7 |' のような進行。1 小節の中の記号は等分し、'-' と '%' は前の和音を延ばす。"""
    spans: List[ChordSpan] = []
    bars = [b.split() for b in text.split("|") if b.strip()]
    position = start
    for tokens in bars:
        step = BAR / len(tokens)
        for token in tokens:
            if token in ("-", "%"):
                spans[-1].dur += step
            else:
                spans.append(ChordSpan(position, step, chord(token)))
            position += step
    return spans


def chord_at(spans: Sequence[ChordSpan], beat: float) -> ChordSpan:
    for s in spans:
        if s.start - 1e-6 <= beat < s.start + s.dur - 1e-6:
            return s
    return spans[-1]


def next_chord(spans: Sequence[ChordSpan], span: ChordSpan) -> ChordSpan:
    i = list(spans).index(span)
    return spans[(i + 1) % len(spans)]


# ---- ドラム ----

DRUM_VELOCITY = {"X": 122, "x": 98, "o": 78, "g": 40, "h": 62}


def drums(patterns: Dict[str, str], start: float = 0.0, bars: int = 1) -> List[Note]:
    """部位ごとの 16 分音符の並び（1 小節 16 文字。'|' で小節を区切って何小節でも書ける）を bars 回くり返す。
    X: 強く、x: ふつう、o: やや弱く、h: 弱く、g: ゴースト、.: なし。"""
    notes = []
    for piece, text in patterns.items():
        steps = text.replace("|", "").replace(" ", "")
        if len(steps) % 16:
            raise ValueError(f"{piece} のパターンが 16 の倍数でない: {len(steps)}")
        length = len(steps) // 16
        for rep in range(0, bars, length):
            for i, ch in enumerate(steps):
                if ch == ".":
                    continue
                at = start + (rep * 16 + i) * SIXTEENTH
                if rep * 16 + i >= bars * 16:
                    break
                notes.append(Note(at, SIXTEENTH, 0, DRUM_VELOCITY[ch], art=piece))
    return notes


def humanize(notes: List[Note], seed: int, timing: float = 0.004, velocity: int = 6, swing: float = 0.0) -> List[Note]:
    """揺らぎ: 時刻（秒の標準偏差）と強さ。swing は裏の 16 分音符を遅らせる割合（0.0〜）。"""
    rng = random.Random(seed)
    out = []
    for n in notes:
        extra = dict(n.extra)
        extra["offset"] = extra.get("offset", 0.0) + rng.gauss(0, timing)
        start = n.start
        position16 = round(n.start / SIXTEENTH)
        if swing and abs(n.start / SIXTEENTH - position16) < 1e-6 and position16 % 2 == 1:
            start += swing * SIXTEENTH
        vel = max(1, min(127, n.vel + rng.randint(-velocity, velocity)))
        out.append(Note(start, n.dur, n.pitch, vel, n.art, extra))
    return out


# ---- ベース ----

def _nearest(pc: int, reference: int, low: int, high: int) -> int:
    """音名 pc の、reference に一番近い low〜high の高さ。"""
    candidates = [p for p in range(low, high + 1) if p % 12 == pc]
    return min(candidates, key=lambda p: (abs(p - reference), p))


def bass(spans: Sequence[ChordSpan], templates: Sequence[str], start: float, bars: int, vel: int = 92,
         low: int = 28, high: int = 45, center: int = 33) -> List[Note]:
    """ベースの型（度数:長さ）を和音に当てはめる。templates は小節ごと（足りなければくり返す）。
    度数: R（根音。分数コードなら下の音）、8（1 オクターブ上）、5、3、7、9（根音の上）、A（次の和音の根音へ半音で近づく）、
    x（ゴースト）、r（休み）。末尾の ! は強く、~ は次の音へつなぐ（レガート）。
    根音は前の根音に近い高さを選ぶ（下がっていく進行で 1 オクターブ跳ばない）。"""
    notes: List[Note] = []
    previous_root = center
    roots = {}
    for b in range(bars):
        template = templates[b % len(templates)]
        position = start + b * BAR
        for token in template.split():
            accent = token.endswith("!")
            legato = token.endswith("~")
            token = token.rstrip("!~")
            degree, length = token.split(":")
            dur = float(length) * SIXTEENTH
            span = chord_at(spans, position)
            if id(span) not in roots:
                roots[id(span)] = _nearest(span.chord.bass, previous_root, low, high)
                previous_root = roots[id(span)]
            root = roots[id(span)]
            if degree == "x":
                notes.append(Note(position, min(dur, SIXTEENTH), root + 12, 70, art="ghost"))
            elif degree != "r":
                if degree == "A":
                    target = _nearest(next_chord(spans, span).chord.bass, root, low, high)
                    last = notes[-1].pitch if notes else root
                    p = target - 1 if last < target else target + 1
                elif degree == "R":
                    p = root
                elif degree == "8":
                    p = root + 12
                else:
                    p = root + (span.chord.degree(degree) - root) % 12
                gate = 1.0 if legato else 0.88
                notes.append(Note(position, dur * gate, p, vel + (14 if accent else 0)))
            position += dur
        if abs(position - (start + (b + 1) * BAR)) > 1e-6:
            raise ValueError(f"ベースの型の長さが 1 小節でない: {template}")
    return notes


# ---- 伴奏（和音を刻む）・パッド・アルペジオ ----

def comp(spans: Sequence[ChordSpan], rhythm: Sequence[str], start: float, bars: int, low: int, high: int,
         count: int = 4, vel: int = 80, rootless: bool = True, top: Optional[int] = None) -> List[Note]:
    """和音を rhythm（'c:3 r:3 c:2 ...'。小節ごと）で刻む。c は鳴らす、a は次の和音を先に鳴らす（食い）、r は休み。
    声部は前の和音から近い配置にする。"""
    notes: List[Note] = []
    previous = None
    for b in range(bars):
        pattern = rhythm[b % len(rhythm)]
        position = start + b * BAR
        for token in pattern.split():
            accent = token.endswith("!")
            kind, length = token.rstrip("!").split(":")
            dur = float(length) * SIXTEENTH
            if kind in ("c", "a"):
                span = chord_at(spans, position)
                if kind == "a":
                    span = next_chord(spans, span)
                pitches = voice(span.chord, low, high, count, previous, rootless=rootless, top=top)
                previous = pitches
                for p in pitches:
                    notes.append(Note(position, dur * 0.92, p, vel + (12 if accent else 0)))
            position += dur
        if abs(position - (start + (b + 1) * BAR)) > 1e-6:
            raise ValueError(f"伴奏のリズムの長さが 1 小節でない: {pattern}")
    return notes


def pad(spans: Sequence[ChordSpan], start: float, end: float, low: int, high: int, count: int = 4,
        vel: int = 70, rootless: bool = False, overlap: float = 0.05) -> List[Note]:
    """和音を長く伸ばす（和音が変わるたび）。"""
    notes: List[Note] = []
    previous = None
    for span in spans:
        if span.start < start - 1e-6 or span.start >= end - 1e-6:
            continue
        pitches = voice(span.chord, low, high, count, previous, rootless=rootless)
        previous = pitches
        dur = min(span.dur, end - span.start)
        notes.extend(Note(span.start, dur + overlap, p, vel) for p in pitches)
    return notes


def arpeggio(spans: Sequence[ChordSpan], start: float, bars: int, low: int, high: int, step: float = SIXTEENTH,
             order: str = "up", vel: int = 70, gate: float = 0.9, accent_every: int = 4) -> List[Note]:
    """和音の構成音を low〜high の中で順に鳴らす（up・down・updown）。"""
    notes: List[Note] = []
    position = start
    i = 0
    end = start + bars * BAR
    while position < end - 1e-6:
        span = chord_at(spans, position)
        tones = sorted({p for p in range(low, high + 1) if p % 12 in span.chord.tones()})
        if order == "down":
            seq = tones[::-1]
        elif order == "updown":
            seq = tones + tones[-2:0:-1]
        else:
            seq = tones
        p = seq[i % len(seq)]
        v = vel + (10 if i % accent_every == 0 else 0)
        notes.append(Note(position, step * gate, p, v))
        position += step
        i += 1
    return notes


def shift(notes: Sequence[Note], beats: float = 0.0, semitones: int = 0) -> List[Note]:
    return [n.shifted(beats, semitones) for n in notes]


def window(notes: Sequence[Note], start: float, end: float) -> List[Note]:
    return [n for n in notes if start - 1e-6 <= n.start < end - 1e-6]
