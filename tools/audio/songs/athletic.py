"""ステージ ATHLETIC の BGM（デモ 2。仮の題「SKYWARD」）。E メジャー、150 BPM。

参考（ユーザー、2026-10-27）: 山本真央樹「GEOMETRIC DANCE」（BPM 150 のプログレッシブ・フュージョン。爽やかで、目まぐるしく展開が
変わる）と、庭師「水晶しようぜ」。デモ 1 への指摘「リズムが一定で、進行にメリハリがなく、のっぺりしている」に対して:

- 旋律: 動機（短い形）をくり返して変える。長く伸ばす音と 16 分の速い動き、休み、食いを混ぜる。跳躍と山を作る。
- 和声: 副属七（G#7 → C#m、E7 → A）、半音で下がるベース（C#m → C#m/B → A#m7b5 → A → G#m → G → F#m）、
  サビで短 3 度上の G メジャーへ転調し、B7（E の属七）で E へ戻る。
- 構成と強弱: 決め（全員で同じリズム）、ハーフタイム、ユニゾン、ソロ、ドラムとローズだけに落とすブレイク、盛り上げ。

構成: 前奏 4（1 回だけ）→ リフ 4 → A 8 → A' 8 → B 8（ハーフタイム）→ サビ 16（G）→ ユニゾン 8 → ソロ 8 → ブレイク 8 → リフへ戻る。
残り 1 分の版（rush=True）は、F メジャー・162 BPM で、リフ → B（刻み）→ サビ → ユニゾン → サビ。
"""

from typing import Dict, List, Sequence, Tuple

import pedalboard as pb

from airerage_audio.arrange import (BAR, ChordSpan, arpeggio, bass, comp, drums, humanize, pad, progression, shift)
from airerage_audio.notation import Note, melody
from airerage_audio.song import Song, Track

PROGRESSIONS = {
    "intro": "| Cmaj9 | D69 | Esus4 | Esus4 |",
    "R": "| E | F#m9 | Amaj9 | B13 |",
    "A": "| Emaj9 | G#m7 C#m7 | Amaj9 | F#m11 B7sus4 | Emaj9 | G#7 | C#m9 - Bm7 E7 | Amaj7 B7sus4 |",
    "B": "| C#m9 | C#m9/B | A#m7b5 | Amaj9 | G#m7 | Gmaj7#11 | F#m11 | B7sus4 B7 |",
    "C": "| Cmaj9 | D/C | Bm7 | Em9 | Am9 | D9sus4 D9 | Gmaj9 | Cmaj7 D/F# "
         "| Cmaj9 | D/C | Bm7 E7b9 | Am9 | F#m7b5 B7b9 | Em9 A13 | Cmaj7 D6 | B7sus4 B7 |",
    "U": "| E | D/E | C/E | D/E | E | D/E | Cmaj7 | Bsus4 B |",
    "S": "| Cmaj9 | D69 | Emaj9 | % | Cmaj9 | D69 | Emaj9 | E/G# |",
    "D": "| Amaj9 | G#m7 | F#m11 | Emaj9/G# | Amaj9 | Bm7 E7 | Amaj9 | B7sus4 |",
}
PROGRESSIONS["A2"] = PROGRESSIONS["A"]
SECTION_BARS = {"intro": 4, "R": 4, "A": 8, "A2": 8, "B": 8, "C": 16, "U": 8, "S": 8, "D": 8}

# リフ（曲の顔。リード・金管・ベースが同じ動きをする。3+3+2 の区切り）
RIFF = ("e5:3 b4:3 e5:2 f#5:3 g#5:3 b5:2 | a5:3 g#5:3 e5:2 f#5:2 e5:2 c#5:2 b4:2 | "
        "c#5:3 e5:3 a5:2 b5:3 c#6:3 e6:2 | d#6:3 c#6:3 b5:2 f#5:2 g#5:2 a5:2 b5:2 |")

MELODIES = {
    "R": RIFF,
    # A: 速い上りから長い音へ（問い）、16 分の動きと 3+3+2（答え）、4 小節目の終わりは休んで金管が答える、
    # G#7 の半音の下り、駆け上がり
    "A": "r:2 b4:1 c#5:1 e5:2 g#5:2 b5:8 | -:4 c#6:2 b5:2 g#5:4 e5:4 | "
         "r:2 e5:1 f#5:1 g#5:2 b5:1 c#6:1 e6:3 c#6:3 b5:2 | a5:6 g#5:2 f#5:4 r:4 | "
         "r:2 b4:1 c#5:1 e5:2 g#5:2 d#6:6 e6:2 | -:4 d#6:2 c6:2 g#5:4 d#5:4 | "
         "e5:3 g#5:3 b5:2 a5:2 f#5:2 g#5:2 d5:2 | c#5:6 r:2 b4:1 c#5:1 e5:1 f#5:1 g#5:1 a5:1 b5:1 c#6:1 |",
    # A': 上から始めて F#6 まで上がり、最後は全員の決め（3 発）で B へ
    "A2": "d#6:6 e6:2 b5:4 g#5:4 | f#5:4 g#5:2 b5:2 e5:8 | "
          "r:2 e5:1 f#5:1 g#5:2 b5:1 c#6:1 e6:3 c#6:3 b5:2 | a5:6 b5:2 c#6:4 r:4 | "
          "g#5:6 b5:2 e6:4 f#6:4 | -:4 e6:2 d#6:2 c6:4 g#5:4 | "
          "c#6:3 b5:3 g#5:2 f#5:2 a5:2 b5:2 g#5:2 | c#6:6 r:2 b5:3 b5:3 b5:2 |",
    # B（ハーフタイム）: 長い音で、半音で下がるベースの上をゆっくり上っていく
    "B": "g#5:8 b5:4 d#6:4 | c#6:12 r:2 b5:1 c#6:1 | e6:4 c#6:4 g#5:4 a#5:4 | b5:12 r:2 g#5:1 a5:1 | "
         "d#6:6 b5:2 f#5:8 | c#6:6 b5:2 f#5:8 | a5:4 b5:4 c#6:4 e6:4 | f#6:6 e6:2 d#6:2 c#6:2 b5:2 a5:2 |",
    # サビ（G メジャー）: 素早く駆け上がって長い E6 へ（動機）。後半は A6 まで上がる
    "C": "r:2 g5:1 a5:1 b5:2 d6:2 e6:8 | -:4 f#6:2 e6:2 d6:4 a5:4 | b5:6 d6:2 f#6:6 e6:2 | "
         "-:4 d6:2 b5:2 g5:4 f#5:4 | e5:2 g5:2 a5:2 c6:2 e6:4 g6:4 | g6:8 f#6:4 e6:2 d6:2 | "
         "b5:4 a5:2 b5:2 d6:6 r:2 | e6:4 d6:2 b5:2 a5:2 b5:2 d6:4 | "
         "r:2 g5:1 a5:1 b5:2 d6:2 e6:8 | -:4 f#6:2 e6:2 d6:2 e6:2 f#6:4 | f#6:6 d6:2 b5:2 g#5:2 f6:2 e6:2 | "
         "-:6 g6:2 e6:4 c6:4 | f#6:3 a6:5 f#6:2 d#6:2 c6:2 a5:2 | b5:4 e6:4 c#6:2 e6:2 f#6:2 g6:2 | "
         "e6:6 d6:2 e6:2 f#6:2 b5:4 | e6:8 d#6:4 r:4 |",
    # ユニゾン: 16 分の速い動きを全員で。4 小節目と 8 小節目は決め
    "U": "e5:1 f#5:1 g#5:1 b5:1 e6:2 b5:1 g#5:1 f#5:2 e5:1 f#5:1 g#5:2 b5:2 | "
         "a5:1 b5:1 d6:1 e6:1 f#6:2 e6:1 d6:1 b5:2 a5:1 b5:1 d6:2 e6:2 | "
         "g5:1 a5:1 c6:1 d6:1 e6:2 d6:1 c6:1 a5:2 g5:1 a5:1 c6:2 d6:2 | "
         "f#5:2 r:2 f#5:2 r:2 a5:3 b5:3 d6:2 | "
         "e6:1 d#6:1 b5:1 g#5:1 e6:2 f#6:2 g#6:3 f#6:3 e6:2 | f#6:1 e6:1 d6:1 a5:1 f#6:2 e6:2 d6:3 b5:3 a5:2 | "
         "e6:3 d6:3 b5:2 g5:3 a5:3 b5:2 | e6:3 e6:3 e6:2 d#6:3 d#6:3 d#6:2 |",
    "S": "g5:2 b5:2 e6:4 d6:2 b5:2 g5:2 e5:2 | f#5:2 a5:2 e6:4 d6:2 b5:2 a5:2 f#5:2 | "
         "b5:8 c#6:1 b5:1 g#5:1 f#5:1 e5:1 f#5:1 g#5:1 b5:1 | c#6:6 d#6:2 e6:8 | "
         "e6:2 d6:2 b5:2 g5:2 a5:2 b5:2 d6:2 e6:2 | f#6:4 e6:2 c#6:2 b5:2 a5:2 f#5:2 e5:2 | "
         "b5:6 c#6:2 b5:2 g#5:2 e5:2 f#5:2 | g#5:1 a5:1 b5:1 c#6:1 d#6:1 e6:1 f#6:1 g#6:1 e6:8 |",
    # ブレイク: 前半はリードなし（ベルがリフの頭をこだまのように）、後半はリフの形で上っていく
    "D": "r:16 | r:16 | r:16 | r:16 | e5:3 b4:3 e5:2 f#5:3 g#5:3 b5:2 | a5:3 f#5:3 d5:2 e5:3 g#5:3 b5:2 | "
         "c#6:3 e6:3 a5:2 b5:3 c#6:3 e6:2 | f#6:8 e6:4 r:4 |",
}
BREAK_BELLS = "e6:3 b5:3 e6:2 r:8 | d#6:3 b5:3 d#6:2 r:8 | c#6:3 a5:3 c#6:2 r:8 | b5:3 g#5:3 b5:2 r:8 |"

# ---- ドラム（16 分の文字列。X 強く・x ふつう・o やや弱く・h 弱く・g ゴースト） ----

INTRO_DRUMS = {
    "kick": "X..X..X..X..X.X.|X..X..X..X..X.X.|X...............|X...............",
    "snare": "X..X..X..X..X.X.|X..X..X..X..X.X.|................|....hhxhXhxhXxXX",
    "crash": "X...............|X...............|X...............|................",
    "hh_pedal": "................|................|....x...x...x...|x...............",
    "hh": "................|................|..h.h.o.o.x.x.X.|................",
    "tom_hi": "................|................|................|..........XX....",
    "tom_lo": "................|................|................|............XxXX",
}
RIFF_DRUMS = {
    "kick": "X..x..x.....x...|X..x..x...x.....|X..x..x.....x...|X..x..x.x.x.x.x.",
    "snare": "....X..g..g.X..g|....X..g..g.X...|....X..g..g.X..g|.....g..X.X.X.X.",
    "bell": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.|x.o.x.o.........",
    "hh_pedal": "..x...x...x...x.|..x...x...x...x.|..x...x...x...x.|................",
    "crash": "X...............|................|X...............|................",
}
GROOVE_A = {
    "kick": "X......x..x.....|x......x..x...x.|X......x..x.....|X.....x...x.....",
    "snare": "....X..g.g..X..g|....X..g.g..X.g.|....X..g.g..X..g|....X..g..XgXxXx",
    "hh": "xhohxhohxhohxhoh|xhohxhohxhohxh.h|xhohxhohxhohxhoh|xhohxhohxh......",
    "hh_open": "................|..............o.|................|................",
}
FILL_HALF = {
    "kick": "X.......x.......",
    "snare": "....X...Xhxh....",
    "tom_hi": "............xx..",
    "tom_lo": "..............Xx",
    "hh": "xhohxhoh........",
}
FILL_FULL = {
    "kick": "X.......x...x...",
    "snare": "hhxhXhxh........",
    "tom_hi": "........XxXx....",
    "tom_lo": "............XxXX",
}
KIME_A2 = {  # 3 拍目から決め（8・11・14）
    "kick": "X.....x.X..X..X.",
    "snare": "....X...X..X..X.",
    "hh": "xhohxhoh........",
    "crash": "........X..X..X.",
}
HALF_TIME = {
    "kick": "X.........x.....|X.....x...x.....",
    "snare": "........X.......|........X.....g.",
    "hh": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.",
    "hh_open": "................|..............o.",
}
BUILD = {  # B の 7 小節目: 8 分で刻んで 16 分へ
    "kick": "X...X...X...X...",
    "snare": "o.o.o.o.xxxxXXXX",
    "hh": "x.x.x.x.........",
}
GROOVE_C = {
    "kick": "X.....x...x...x.|X.....x...x.x...",
    "snare": "....X......gX...|....X..g...gX..g",
    "ride": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.",
    "hh_pedal": "....x.......x...|....x.......x...",
}
UNISON = {  # 1〜3 小節は刻み、4 小節目は決め（0・4・8・11・14）
    "kick": "X.....x.X.....x.|X.....x.X.....x.|X.....x.X.....x.|X...X...X..X..X.",
    "snare": "....X.......X...|....X.......X...|....X.......X.Xg|....X...X..X..X.",
    "hh": "xhxhxhxhxhxhxhxh|xhxhxhxhxhxhxhxh|xhxhxhxhxhxhxhxh|................",
    "crash": "X...............|................|................|X...X...X..X..X.",
}
UNISON_END = {  # 8 小節目の決め（0・3・6・8・11・14）
    "kick": "X..X..X.X..X..X.",
    "snare": "X..X..X.X..X..X.",
    "crash": "X.......X.......",
}
GROOVE_S = {
    "kick": "X..x..x...x..x..|X..x..x...xx..x.",
    "snare": "....X..g.g..X.g.|..g.X..g.g..X...",
    "bell": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.",
    "hh_pedal": "..x...x...x...x.|..x...x...x...x.",
}
BREAK_DRUMS = {
    "kick": "o...............|................|o...............|................|"
            "X.....o...o.....|X.....o...o.....|X...X...X...X...|X...X...X...X...",
    "hh": "x.h.x.h.x.h.x.h.|x.h.x.h.x.h.x.h.|x.h.x.h.x.h.x.h.|x.h.x.h.x.h.x.h.|"
          "x.h.x.h.x.h.x.h.|x.h.x.h.x.h.x.h.|x.x.x.x.........|................",
    "xstick": "................|................|................|................|"
              "....x.......x...|....x.......x...|................|................",
    "snare": "................|................|................|................|"
             "................|................|o.o.o.o.x.x.x.x.|hhxxxxxxXXXXXXXX",
}
GROOVE_RUSH = {
    "kick": "X...x...X...x..x|X...x...X..xx...",
    "snare": "....X..g....X.g.|....X..g..g.X.gg",
    "hh": "xhohxhohxhohxhoh|xhohxhohxhohxhoh",
    "hh_open": "..o...o...o...o.|..o...o...o...o.",
}


def _sequence(start: float, segments: Sequence[Tuple[Dict[str, str], int]], crashes: Sequence[int] = ()) -> List[Note]:
    """ドラムのパターンを順に並べる（(パターン, 小節の数) の並び）。crashes の小節の頭にクラッシュ。"""
    notes: List[Note] = []
    position = start
    for patterns, bars in segments:
        notes += drums(patterns, position, bars)
        position += bars * BAR
    notes += [Note(start + b * BAR, 0.25, 0, 112, art="crash") for b in crashes]
    return notes


def _section_drums(name: str, start: float, rush: bool) -> List[Note]:
    if name == "intro":
        return drums(INTRO_DRUMS, start, 4)
    if name == "R":
        return drums(RIFF_DRUMS, start, 4)
    if name in ("A", "A2"):
        groove = GROOVE_RUSH if rush else GROOVE_A
        return _sequence(start, [(groove, 4), (groove, 3), (FILL_HALF if name == "A" else KIME_A2, 1)], crashes=(0,))
    if name == "B":
        if rush:
            return _sequence(start, [(GROOVE_C, 6), (BUILD, 1), (FILL_FULL, 1)], crashes=(0, 4))
        return _sequence(start, [(HALF_TIME, 6), (BUILD, 1), (FILL_FULL, 1)], crashes=(0,))
    if name == "C":
        groove = GROOVE_RUSH if rush else GROOVE_C
        return _sequence(start, [(groove, 7), (FILL_HALF, 1), (groove, 7), (FILL_FULL, 1)], crashes=(0, 4, 8, 12))
    if name == "U":
        return _sequence(start, [(UNISON, 4), (UNISON, 3), (UNISON_END, 1)])
    if name == "S":
        return _sequence(start, [(GROOVE_S, 7), (FILL_HALF, 1)], crashes=(0, 4))
    if name == "D":
        return drums(BREAK_DRUMS, start, 8)
    raise KeyError(name)


# ---- ベース・伴奏 ----

A_BASS = [
    "R:3! R:3 8:2 r:2 5:2 R:2 x:1 R:1",
    "R:3 x:1 R:2 5:2 R:3! x:1 8:2 A:2",
    "R:3! R:3 8:2 r:2 5:2 R:2 x:1 R:1",
    "R:3 x:1 R:2 8:2 R:3! x:1 7:2 A:2",
    "R:3! R:3 8:2 r:2 5:2 R:2 x:1 R:1",
    "R:3! R:3 8:2 R:2 3:2 5:2 A:2",
    "R:3! R:3 8:2 R:2 5:2 R:2 A:2",
]
BASS = {
    "intro": ["R:3! R:3 R:3 R:3 R:2 R:2", "R:3! R:3 R:3 R:3 R:2 R:2", "R:16", "r:16"],
    "A": A_BASS + ["R:3! R:3 8:2 R:3 R:3 8:2"],
    "A2": A_BASS + ["R:6! r:2 R:3! R:3 R:2"],
    "B": ["R:8! r:2 8:2 5:2 A:2"] * 6 + ["R:2! R:2 R:2 R:2 8:2 R:2 5:2 A:2", "R:2! R:2 R:2 R:2 R:2 R:2 R:2 A:2"],
    "C": ["R:3! R:3 R:2 8:2 R:2 5:2 7:2", "R:3! R:3 8:2 x:1 R:1 5:2 8:2 A:2"],
    "S": ["R:2! 8:1 R:1 x:1 R:1 5:2 8:2 R:1 x:1 R:2 A:2"],
    "D": ["r:16"] * 4 + ["R:8! R:6 A:2", "R:4! R:4 R:4 A:4", "R:2! R:2 R:2 R:2 R:2 R:2 R:2 A:2",
                         "R:2 R:2 R:2 R:2 R:2 R:2 R:2 r:2"],
}
BASS_RUSH = ["R:2! 8:2 R:2 8:2 R:2 8:2 5:2 A:2"]

A_RHODES = ["c:6 r:4 c:2 r:2 c:2", "r:2 c:2 r:4 c:3 c:3 r:2"] * 3 + ["c:6 r:4 c:2 r:2 c:2"]
RHODES = {
    "intro": ["c:3! c:3 c:3 c:3 c:2 c:2", "c:3! c:3 c:3 c:3 c:2 c:2", "c:16", "r:16"],
    "R": ["c:3! c:3 c:2 r:8"],
    "A": A_RHODES + ["c:6 r:2 c:3 c:3 c:2"],
    "A2": A_RHODES + ["c:6 r:2 c:3! c:3 c:2"],
    "B": ["c:16"] * 6 + ["c:2 c:2 c:2 c:2 c:2 c:2 c:2 c:2", "c:2! c:2 c:2 c:2 c:2 c:2 c:2 c:2"],
    "C": ["c:8 c:6 a:2"],
    "U": ["c:2! r:6 c:2 r:6"] * 3 + ["c:2! r:2 c:2 r:2 c:3 c:3 c:2"] + ["c:2! r:6 c:2 r:6"] * 3 +
         ["c:3! c:3 c:2 c:3 c:3 c:2"],
    "S": ["c:3 c:3 r:2 c:3 c:3 r:2"],
}

BRASS = {
    "A": ["r:16"] * 3 + ["r:12 c:2 c:2"] + ["r:16"] * 4,
    "intro": ["c:3! c:3 c:3 c:3 c:2 c:2", "c:3! c:3 c:3 c:3 c:2 c:2", "c:16", "r:16"],
    "A2": ["r:16"] * 3 + ["r:12 c:2 c:2"] + ["r:16"] * 3 + ["r:8 c:3! c:3 c:2"],
    "B": ["r:16"] * 6 + ["c:16", "c:8 c:8"],
    "C": ["c:3! r:13", "r:12 c:2 a:2"] * 7 + ["c:3! r:13", "c:8! c:4 r:4"],
    "U": ["r:16"] * 3 + ["c:2! r:2 c:2 r:2 c:3 c:3 c:2"] + ["r:16"] * 3 + ["c:3! c:3 c:2 c:3 c:3 c:2"],
    "S": ["c:3! c:3 c:10", "c:3! c:3 c:10", "r:16", "r:16"] * 2,
}


# 場面ごとの強弱（始まりと終わりの dB。仕上げの前に掛ける。全体のラウドネスは後でそろえる）
DYNAMICS = {"intro": (0.0, 0.0), "R": (0.0, 0.0), "A": (-2.5, -2.5), "A2": (-2.0, -1.0), "B": (-3.0, 0.0),
            "C": (0.5, 0.5), "U": (0.0, 0.0), "S": (-1.0, -1.0), "D": (-2.0, 0.0)}
RUSH_DYNAMICS = {"intro": (0.0, 0.0), "R": (0.0, 0.0), "B": (-2.0, 0.0), "C": (0.5, 0.5), "U": (0.0, 0.0)}


def _spans(order: List[str]) -> List[ChordSpan]:
    spans: List[ChordSpan] = []
    position = 0.0
    for name in order:
        spans += progression(PROGRESSIONS[name], position)
        position += SECTION_BARS[name] * BAR
    # 最後の和音の「次」は本体の頭（ループで戻る先）。
    body_first = next(s for s in spans if s.start >= SECTION_BARS[order[0]] * BAR)
    spans.append(ChordSpan(position, BAR, body_first.chord))
    return spans


def build(rush: bool = False) -> Song:
    order = ["intro", "R", "B", "C", "U", "C"] if rush else ["intro", "R", "A", "A2", "B", "C", "U", "S", "D"]
    spans = _spans(order)
    parts: Dict[str, List[Note]] = {k: [] for k in ("drums", "bass", "rhodes", "pad", "strings", "bells", "brass",
                                                    "lead", "solo", "unison", "dxep", "arp")}
    dynamics = []
    position = 0.0
    for name in order:
        bars = SECTION_BARS[name]
        end = position + bars * BAR
        first, last = (RUSH_DYNAMICS if rush else DYNAMICS)[name]
        dynamics += [(position, first), (end - 0.05, last)]
        parts["drums"] += _section_drums(name, position, rush)
        tune = melody(MELODIES[name], position, vel=96) if name in MELODIES else []
        # ベース: リフとユニゾンは旋律の 3 オクターブ下（決めの小節は根音）、ほかは型
        if name == "R":
            parts["bass"] += [Note(n.start, n.dur * 0.9, n.pitch - 36, 100) for n in tune]
        elif name == "U":
            for n in tune:
                bar = int((n.start - position) // BAR)
                if bar not in (3, 7):
                    parts["bass"] += [Note(n.start, n.dur * 0.9, n.pitch - 36, 100)]
            for bar, template in ((3, "R:2! r:2 R:2 r:2 R:3 R:3 R:2"), (7, "R:3! R:3 R:2 R:3 R:3 R:2")):
                parts["bass"] += bass(spans, [template], position + bar * BAR, 1)
        else:
            templates = BASS_RUSH if rush and name in ("B", "C") else BASS[name]
            parts["bass"] += bass(spans, templates, position, bars)
        if name in RHODES:
            parts["rhodes"] += comp(spans, RHODES[name], position, bars, 52, 74, count=4, vel=78)
        if name == "D":
            parts["rhodes"] += arpeggio(spans, position, 4, 57, 78, step=0.5, order="updown", vel=66)
            parts["rhodes"] += comp(spans, ["c:8 c:8"], position + 4 * BAR, 4, 52, 74, count=4, vel=74)
        if name in BRASS:
            parts["brass"] += comp(spans, BRASS[name], position, bars, 60, 81, count=4, vel=92, rootless=False)
        if name == "R":
            parts["brass"] += [Note(n.start, n.dur * 0.85, n.pitch - 12, 100) for n in tune]
        if name in ("A2", "B", "C", "S", "D"):
            parts["pad"] += pad(spans, position, end, 55, 79, count=4, vel=60)
        if name in ("intro", "B", "C"):
            parts["strings"] += pad(spans, position, end, 64, 88, count=3, vel=58 if name != "C" else 70)
        if name == "R":
            parts["bells"] += [Note(n.start, n.dur, n.pitch + 12, 60) for n in tune]
        if name == "A2":
            parts["bells"] += arpeggio(spans, position, bars, 76, 93, step=0.5, order="updown", vel=50)
        if name in ("C", "S"):
            parts["bells"] += arpeggio(spans, position, bars, 76, 95, step=0.25, order="updown", vel=46)
        if name == "D":
            parts["bells"] += melody(BREAK_BELLS, position, vel=70)
        if name == "C":
            parts["dxep"] += comp(spans, RHODES["C"], position, bars, 64, 86, count=3, vel=70)
        if rush and name not in ("intro", "R"):
            parts["arp"] += arpeggio(spans, position, bars, 62, 81, step=0.25, order="up", vel=72, gate=0.45)
        if name in ("R", "A", "A2", "B", "C", "U", "D"):
            parts["lead"] += tune
        if name in ("R", "U"):
            parts["unison"] += tune  # 速い動きの粒をはっきりさせる（立ち上がりの速い音色を重ねる）
        if name == "S":
            parts["solo"] += melody(MELODIES[name], position, vel=100)
        position = end
    intro = SECTION_BARS["intro"] * BAR
    body = position - intro

    hp = pb.HighpassFilter
    tracks = [
        Track("drums", "drums", humanize(parts["drums"], 1, 0.003, 6, swing=0.04), -17.0,
              [pb.Compressor(threshold_db=-16, ratio=2.5, attack_ms=8, release_ms=120),
               pb.HighShelfFilter(cutoff_frequency_hz=8000, gain_db=1.0)], hall=0.04),
        Track("bass", "bass", humanize(parts["bass"], 2, 0.004, 5, swing=0.04), -20.0,
              [hp(cutoff_frequency_hz=32), pb.Compressor(threshold_db=-22, ratio=4, attack_ms=5, release_ms=90),
               pb.LowShelfFilter(cutoff_frequency_hz=90, gain_db=2.0), pb.PeakFilter(cutoff_frequency_hz=260, gain_db=-2.5, q=1.0),
               pb.HighShelfFilter(cutoff_frequency_hz=2500, gain_db=2.0)], duck=2.5),
        Track("rhodes", "rhodes", humanize(parts["rhodes"], 3, 0.006, 6, swing=0.04), -23.0,
              [hp(cutoff_frequency_hz=110), pb.Chorus(rate_hz=0.6, depth=0.2, mix=0.3),
               pb.Compressor(threshold_db=-20, ratio=2.5, attack_ms=15, release_ms=150)],
              width=1.2, hall=0.12, delay=0.04),
        Track("pad", "surge:Pads/MKS-70 Warm Pad", parts["pad"], -28.0,
              [hp(cutoff_frequency_hz=200), pb.LowpassFilter(cutoff_frequency_hz=9000)], width=1.3, hall=0.25, duck=2.0),
        Track("strings", "surge:Polysynths/Juno-60 Strings", parts["strings"], -29.0, [hp(cutoff_frequency_hz=300)],
              width=1.2, hall=0.3),
        Track("bells", "surge:Plucks/Bell 2", humanize(parts["bells"], 4, 0.002, 6), -30.0, [hp(cutoff_frequency_hz=600)],
              pan=0.25, width=1.4, hall=0.25, delay=0.25),
        Track("brass", "surge:Brass/Toto Brass", humanize(parts["brass"], 5, 0.003, 4), -23.5,
              [hp(cutoff_frequency_hz=160), pb.Compressor(threshold_db=-18, ratio=3, attack_ms=10, release_ms=120)], hall=0.15),
        Track("lead", "surge:Leads/Moogy Saw", humanize(parts["lead"], 6, 0.003, 4), -19.5,
              [hp(cutoff_frequency_hz=200), pb.Compressor(threshold_db=-20, ratio=3, attack_ms=8, release_ms=120),
               pb.PeakFilter(cutoff_frequency_hz=3000, gain_db=1.5, q=0.8)], hall=0.14, delay=0.12, vibrato=0.18),
        Track("lead2", "surge:Leads/Smoothness World Cup", humanize(parts["lead"], 6, 0.003, 4), -26.0,
              [hp(cutoff_frequency_hz=200)], hall=0.15, vibrato=0.18),
        Track("unison", "surge:Leads/Synth Guitar 1", humanize(parts["unison"], 6, 0.003, 4), -25.0,
              [hp(cutoff_frequency_hz=250)], width=0.8, hall=0.1),
        Track("solo", "surge:Leads/Synth Guitar 1", humanize(parts["solo"], 7, 0.003, 4), -20.0,
              [hp(cutoff_frequency_hz=200), pb.Compressor(threshold_db=-20, ratio=3, attack_ms=8, release_ms=120)],
              hall=0.15, delay=0.15, vibrato=0.22),
        Track("dxep", "surge:Keys/DX EP", humanize(parts["dxep"], 8, 0.004, 5), -28.0, [hp(cutoff_frequency_hz=400)],
              pan=-0.2, width=1.3, hall=0.2, delay=0.1),
    ]
    if rush:
        tracks.append(Track("arp", "surge:Plucks/The 1980s", humanize(parts["arp"], 9, 0.002, 5), -27.0,
                            [hp(cutoff_frequency_hz=300)], pan=-0.15, width=1.3, hall=0.15, delay=0.2, duck=2.0))
    tracks = [t for t in tracks if t.notes]
    return Song("athletic_rush" if rush else "athletic", 162 if rush else 150, intro, body, tracks,
                transpose=1 if rush else 0, dynamics=dynamics)
