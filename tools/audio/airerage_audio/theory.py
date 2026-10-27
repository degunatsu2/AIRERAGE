"""和音の記号と、声部の配置（ボイシング）。"""

import itertools
import re
from dataclasses import dataclass
from typing import List, Optional, Sequence

PITCH_CLASS = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}

# 和音の種類 → 根音からの音程（半音）。並びは「残す順」（先にあるほど大事）。
QUALITIES = {
    "": [0, 4, 7],
    "add9": [0, 4, 7, 14],
    "6": [0, 4, 9, 7],
    "69": [0, 4, 9, 14, 7],
    "maj7": [0, 4, 11, 7],
    "maj9": [0, 4, 11, 14, 7],
    "maj7#11": [0, 4, 11, 18, 7],
    "m": [0, 3, 7],
    "m7": [0, 3, 10, 7],
    "m9": [0, 3, 10, 14, 7],
    "m11": [0, 3, 10, 17, 14, 7],
    "m7b5": [0, 3, 10, 6],
    "7": [0, 4, 10, 7],
    "9": [0, 4, 10, 14, 7],
    "13": [0, 4, 10, 21, 14],
    "7b9": [0, 4, 10, 13, 7],
    "sus4": [0, 5, 7],
    "sus2": [0, 2, 7],
    "7sus4": [0, 5, 10, 7],
    "9sus4": [0, 5, 10, 14, 7],
    "dim7": [0, 3, 6, 9],
}


@dataclass(frozen=True)
class Chord:
    symbol: str
    root: int  # 0〜11
    intervals: tuple
    bass: int  # 0〜11（分数コードの下の音。なければ根音）

    def tones(self) -> List[int]:
        """構成音の音名（0〜11。大事な順）。"""
        out = []
        for i in self.intervals:
            pc = (self.root + i) % 12
            if pc not in out:
                out.append(pc)
        return out

    def degree(self, name: str) -> int:
        """ベースの度数の記号（R・3・5・7・8・9）の音名（0〜11）。"""
        third = 3 if 3 in self.intervals else (5 if 5 in self.intervals and 4 not in self.intervals else 4)
        seventh = 11 if 11 in self.intervals else (10 if 10 in self.intervals else (9 if 9 in self.intervals else 12))
        fifth = 6 if 6 in self.intervals and 7 not in self.intervals else 7
        table = {"R": 0, "8": 0, "3": third, "5": fifth, "7": seventh % 12, "9": 2}
        if name in ("R", "8"):
            return self.bass
        return (self.root + table[name]) % 12


def parse_pc(text: str) -> int:
    pc = PITCH_CLASS[text[0].upper()]
    for ch in text[1:]:
        pc += 1 if ch == "#" else -1
    return pc % 12


def chord(symbol: str) -> Chord:
    m = re.fullmatch(r"([A-G][#b]?)([^/]*)(?:/([A-G][#b]?))?", symbol)
    if not m:
        raise ValueError(f"和音の記号が読めない: {symbol}")
    root = parse_pc(m.group(1))
    quality = m.group(2)
    if quality not in QUALITIES:
        raise ValueError(f"和音の種類が分からない: {symbol}")
    bass = parse_pc(m.group(3)) if m.group(3) else root
    return Chord(symbol, root, tuple(QUALITIES[quality]), bass)


def voice(target: Chord, low: int, high: int, count: int, previous: Optional[Sequence[int]] = None,
          rootless: bool = False, top: Optional[int] = None) -> List[int]:
    """和音を low〜high の count 音に配置する。前の配置から動きが少なく、top があれば最高音がそれに近いものを選ぶ。"""
    tones = target.tones()
    if len(tones) < count and target.bass not in tones:
        tones.append(target.bass)  # 分数コードは下の音も上で鳴らせる（B/A なら A）
    if rootless and len(tones) > count:
        tones = [t for t in tones if t != target.root] + [target.root]
    chosen = tones[:count]
    while len(chosen) < count:
        chosen.append(tones[len(chosen) % len(tones)])
    best = None
    best_cost = None
    for perm in set(itertools.permutations(chosen)):
        # 下から順に、前の音より上で一番近い高さに置く（隣り合う音は 1〜12 半音）。
        for start in range(low, low + 12):
            if start % 12 != perm[0]:
                continue
            notes = [start]
            for pc in perm[1:]:
                p = notes[-1] + 1
                while p % 12 != pc:
                    p += 1
                notes.append(p)
            if notes[-1] > high:
                continue
            if any(b - a == 1 and i == 0 for i, (a, b) in enumerate(zip(notes, notes[1:]))):
                continue  # 一番下の 2 音の短 2 度は濁るので避ける
            cost = 0.0
            if previous:
                prev = sorted(previous)
                cost += sum(abs(a - b) for a, b in zip(sorted(notes), prev[:len(notes)]))
            else:
                cost += abs((notes[0] + notes[-1]) / 2 - (low + high) / 2)
            if top is not None:
                cost += 3 * abs(notes[-1] - top)
            if best_cost is None or cost < best_cost:
                best, best_cost = notes, cost
    if best is None:
        if count > 2:
            return voice(target, low, high, count - 1, previous, rootless, top)
        raise ValueError(f"{target.symbol} を {low}〜{high} に {count} 音で置けない")
    return best
