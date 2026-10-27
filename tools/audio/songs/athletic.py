"""ステージ ATHLETIC の BGM（デモ。仮の題「SKYWARD」）。開放感と透明感のあるフュージョン。E メジャー、150 BPM。

構成: 前奏 4 小節（♭VI-♭VII の決め）→ A 8 → A' 8 → B 8（王道進行）→ サビ 16（カノン進行）→ ソロ 16（♭VI-♭VII-I）→ A へ戻る。
残り 1 分の版（rush=True）は、F メジャー・162 BPM で、B → サビ → ソロの前半 → サビをくり返し、16 分のアルペジオと強いドラムを足す。
"""

from typing import Dict, List

import pedalboard as pb

from airerage_audio.arrange import (BAR, ChordSpan, arpeggio, bass, comp, drums, humanize, pad, progression)
from airerage_audio.notation import Note, melody
from airerage_audio.song import Song, Track

PROGRESSIONS = {
    "intro": "| Cmaj9 | D69 | Eadd9 | B9sus4 |",
    "A": "| Emaj9 | % | C#m9 | % | Amaj9 | % | F#m11 | B9sus4 |",
    "A2": "| Emaj9 | % | C#m9 | % | Amaj9 | % | F#m11 | B9sus4 B7 |",
    "B": "| Amaj7 | B/A | G#m7 | C#m7 | F#m7 | G#m7 | Amaj7 | Bsus4 B |",
    "C": "| E | B/D# | C#m7 | G#m/B | Amaj7 | E/G# | F#m7 | B7sus4 B7 "
         "| E | B/D# | C#m7 | G#m/B | Amaj7 | B/A | G#m7 C#m7 | F#m7 B7sus4 |",
    "S": "| Cmaj9 | D69 | Emaj9 | % | Cmaj9 | D69 | Emaj9 | E/G# "
         "| Amaj9 | G#m7 | F#m11 | E/G# | Amaj9 | B/A | Bsus4 | B7 |",
    "S8": "| Cmaj9 | D69 | Emaj9 | % | Cmaj9 | D69 | Emaj9 | E/G# |",
}

A_HEAD = ("r:2 b4:2 e5:2 f#5:2 g#5:4 b5:4 | -:6 a5:2 g#5:2 f#5:2 e5:2 f#5:2 | "
          "g#5:6 e5:2 d#5:4 c#5:4 | b4:8 r:2 c#5:2 d#5:2 e5:2 | ")
MELODIES = {
    "A": A_HEAD + "f#5:3 g#5:3 a5:2 g#5:4 e5:4 | b5:6 g#5:2 f#5:4 e5:4 | a5:3 g#5:3 f#5:2 e5:4 c#5:4 | e5:2 f#5:10 r:4 |",
    "A2": A_HEAD + "f#5:3 g#5:3 a5:2 b5:4 c#6:4 | b5:6 a5:2 g#5:4 f#5:4 | e5:3 f#5:3 g#5:2 a5:4 b5:4 | "
                   "c#6:4 b5:4 a5:2 g#5:2 f#5:2 d#5:2 |",
    "B": "r:2 c#5:2 e5:2 g#5:4 e5:2 c#5:2 e5:2 | f#5:6 d#5:2 b4:8 | r:2 b4:2 d#5:2 f#5:4 d#5:2 b4:2 d#5:2 | "
         "e5:6 c#5:2 g#4:8 | r:2 a4:2 c#5:2 e5:2 f#5:2 a5:4 g#5:2 | b5:6 a5:2 g#5:4 f#5:4 | "
         "e5:2 f#5:2 g#5:2 a5:2 b5:2 c#6:2 d#6:2 e6:2 | e6:8 d#6:8 |",
    "C": "b5:6 g#5:2 e6:6 d#6:2 | -:4 c#6:2 b5:2 f#5:8 | r:2 e5:2 g#5:2 b5:2 c#6:4 b5:2 c#6:2 | "
         "d#6:6 c#6:2 b5:4 g#5:4 | a5:6 g#5:2 a5:4 c#6:4 | b5:6 e5:2 g#5:4 b5:4 | a5:4 g#5:4 f#5:4 e5:4 | "
         "f#5:8 g#5:2 a5:2 b5:2 d#6:2 | e6:6 d#6:2 b5:4 g#5:4 | f#5:6 g#5:2 b5:4 d#6:4 | e6:6 d#6:2 c#6:4 b5:4 | "
         "d#6:6 b5:2 g#5:8 | a5:4 b5:4 c#6:4 e6:4 | f#6:8 e6:4 d#6:4 | d#6:6 b5:2 c#6:4 e6:4 | e6:6 c#6:2 b5:8 |",
    "S": "g5:2 b5:2 e6:4 d6:2 b5:2 g5:2 e5:2 | f#5:2 a5:2 e6:4 d6:2 b5:2 a5:2 f#5:2 | "
         "b5:8 c#6:1 b5:1 g#5:1 f#5:1 e5:1 f#5:1 g#5:1 b5:1 | c#6:6 d#6:2 e6:8 | "
         "e6:2 d6:2 b5:2 g5:2 a5:2 b5:2 d6:2 e6:2 | f#6:4 e6:2 c#6:2 b5:2 a5:2 f#5:2 e5:2 | "
         "b5:6 c#6:2 b5:2 g#5:2 e5:2 f#5:2 | g#5:1 a5:1 b5:1 c#6:1 d#6:1 e6:1 f#6:1 g#6:1 e6:8 | "
         "c#6:6 b5:2 a5:4 g#5:4 | b5:6 a5:2 g#5:4 f#5:4 | a5:6 g#5:2 f#5:4 e5:4 | g#5:8 r:4 b4:2 c#5:2 | "
         "e5:2 f#5:2 g#5:2 b5:2 c#6:4 e6:4 | d#6:6 c#6:2 b5:4 f#5:4 | e6:12 r:4 | r:8 d#6:2 c#6:2 b5:2 a5:2 |",
}
MELODIES["S8"] = " | ".join(MELODIES["S"].split(" | ")[:8]) + " |"

# ---- ドラム ----

GROOVE_A = {
    "kick": "X......x..x.....|x......x..x...x.",
    "snare": "....X..g.g..X..g|....X..g.g..X.g.",
    "hh": "xhohxhohxhohxhoh|xhohxhohxhohxh.h",
    "hh_open": "................|..............o.",
}
GROOVE_B = {
    "kick": "X..x..x...x..x..|X..x..x...x.....",
    "snare": "....X..g..g.X...|....X..g..g.X.gg",
    "hh": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x...",
    "hh_open": "................|..............o.",
}
GROOVE_C = {
    "kick": "X.....x...x...x.|X.....x...x.x...",
    "snare": "....X......gX...|....X..g...gX..g",
    "ride": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.",
    "hh_pedal": "....x.......x...|....x.......x...",
}
GROOVE_S = {
    "kick": "X..x..x...x..x..|X..x..x...xx..x.",
    "snare": "....X..g.g..X.g.|..g.X..g.g..X...",
    "bell": "x.o.x.o.x.o.x.o.|x.o.x.o.x.o.x.o.",
    "hh_pedal": "..x...x...x...x.|..x...x...x...x.",
}
GROOVE_RUSH = {
    "kick": "X...x...X...x..x|X...x...X..xx...",
    "snare": "....X..g....X.g.|....X..g..g.X.gg",
    "hh": "xhohxhohxhohxhoh|xhohxhohxhohxhoh",
    "hh_open": "..o...o...o...o.|..o...o...o...o.",
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
KIME_B = {
    "kick": "X..X..X...X.X.X.",
    "snare": "...X..X...X.X.X.",
    "crash": "X.............x.",
}
INTRO_DRUMS = {
    "kick": "X..X..X.........|X..X..X.........|X...............|X...........X...",
    "snare": "...X..X.........|...X..X.........|................|X.......xhxh....",
    "crash": "X.....X.........|X.....X.........|X...............|X...............",
    "hh_pedal": "........x...x...|........x...x...|....x...x...x...|....x...........",
    "hh": "................|................|..h.h.o.o.x.x.X.|................",
    "tom_hi": "................|................|................|............xx..",
    "tom_lo": "................|................|................|..............XX",
}


def _groove(groove: Dict[str, str], start: float, bars: int, last: Dict[str, str] = None,
            crashes=(0,)) -> List[Note]:
    body = bars - (1 if last else 0)
    notes = drums(groove, start, body)
    if last:
        notes += drums(last, start + body * BAR, 1)
    for bar in crashes:
        notes.append(Note(start + bar * BAR, 0.25, 0, 112, art="crash"))
    return notes


SECTION_BARS = {"intro": 4, "A": 8, "A2": 8, "B": 8, "C": 16, "S": 16, "S8": 8}


def _section_drums(name: str, start: float, rush: bool) -> List[Note]:
    if name == "intro":
        return drums(INTRO_DRUMS, start, 4)
    if name in ("A", "A2"):
        return _groove(GROOVE_RUSH if rush else GROOVE_A, start, 8, FILL_HALF if name == "A" else FILL_FULL)
    if name == "B":
        return _groove(GROOVE_C if rush else GROOVE_B, start, 8, KIME_B)
    if name == "C":
        groove = GROOVE_RUSH if rush else GROOVE_C
        return (_groove(groove, start, 8, FILL_HALF, crashes=(0, 4)) +
                _groove(groove, start + 8 * BAR, 8, FILL_FULL, crashes=(0, 4)))
    if name == "S":
        return (_groove(GROOVE_S, start, 8, FILL_HALF) + _groove(GROOVE_S, start + 8 * BAR, 8, FILL_FULL))
    if name == "S8":
        return _groove(GROOVE_S, start, 8, FILL_FULL, crashes=(0, 4))
    raise KeyError(name)


# ---- ベース・伴奏・パッドなど ----

BASS = {
    "intro": ["R:3! R:3 R:10", "R:3! R:3 R:10", "R:16", "R:4! r:4 5:2 7:2 8:2 A:2"],
    "A": ["R:3! R:3 8:2 r:2 5:2 R:2 x:1 R:1", "R:3 x:1 R:2 8:2 r:2 7:2 5:2 A:2"],
    "A2": ["R:3! R:3 8:2 r:2 5:2 R:2 x:1 R:1", "R:3 x:1 R:2 8:2 r:2 7:2 5:2 A:2"] * 3 +
          ["R:3! R:3 8:2 r:2 5:2 R:2 x:1 R:1", "R:2! 8:2 7:2 5:2 R:2 3:2 5:2 A:2"],
    "B": ["R:2! x:1 R:1 8:2 R:2 x:1 R:1 5:2 8:2 A:2"] * 7 + ["R:3! R:3 R:4 8:2 8:2 8:2"],
    "C": ["R:3! R:3 R:2 8:2 R:2 5:2 7:2", "R:3! R:3 8:2 x:1 R:1 5:2 8:2 A:2"],
    "S": ["R:2! 8:1 R:1 x:1 R:1 5:2 8:2 R:1 x:1 R:2 A:2"],
}
BASS["S8"] = BASS["S"]
BASS_RUSH = ["R:2! 8:2 R:2 8:2 R:2 8:2 5:2 A:2"]

RHODES = {
    "intro": ["c:3! c:3 c:10", "c:3! c:3 c:10", "c:16", "c:4! r:12"],
    "A": ["c:6 r:4 c:2 r:2 c:2", "r:2 c:2 r:4 c:3 c:3 r:2"],
    "B": ["c:3 c:3 r:2 c:2 r:2 c:2 r:2", "r:2 c:2 r:2 c:2 c:3 c:3 r:2"] * 3 +
         ["c:3 c:3 r:2 c:2 r:2 c:2 r:2", "c:3! c:3 c:4 c:2 c:2 c:2"],
    "C": ["c:8 c:6 a:2"],
    "S": ["c:3 c:3 r:2 c:3 c:3 r:2"],
}
RHODES["A2"] = RHODES["A"]
RHODES["S8"] = RHODES["S"]

BRASS = {
    "intro": ["c:3! c:3 c:10", "c:3! c:3 c:10", "c:16", "c:3! r:13"],
    "B": ["r:16", "r:10 c:2 r:2 c:2"] * 3 + ["r:16", "c:3! c:3 c:4 c:2 c:2 c:2"],
    "C": ["c:3! r:13", "r:12 c:2 a:2"],
    "S": ["c:3! c:3 c:10", "c:3! c:3 c:10", "r:16", "r:16"] * 2 + ["r:16"] * 6 + ["c:12! r:4", "r:16"],
}
BRASS["S8"] = BRASS["S"][:8]


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
    order = ["intro", "B", "C", "S8", "C"] if rush else ["intro", "A", "A2", "B", "C", "S"]
    spans = _spans(order)
    parts: Dict[str, List[Note]] = {k: [] for k in
                                    ("drums", "bass", "rhodes", "pad", "strings", "bells", "brass", "lead", "solo",
                                     "dxep", "arp")}
    position = 0.0
    for name in order:
        bars = SECTION_BARS[name]
        end = position + bars * BAR
        parts["drums"] += _section_drums(name, position, rush)
        bass_templates = BASS_RUSH if rush and name in ("C",) else BASS[name]
        parts["bass"] += bass(spans, bass_templates, position, bars)
        parts["rhodes"] += comp(spans, RHODES[name], position, bars, 52, 74, count=4, vel=78)
        if name in BRASS:
            parts["brass"] += comp(spans, BRASS[name], position, bars, 60, 81, count=4, vel=92, rootless=False)
        if name != "A":
            parts["pad"] += pad(spans, position, end, 55, 79, count=4, vel=64)
        if name in ("intro", "B", "C"):
            parts["strings"] += pad(spans, position, end, 64, 88, count=3, vel=58 if name != "C" else 70)
        if name == "A2":
            parts["bells"] += arpeggio(spans, position, bars, 76, 93, step=0.5, order="updown", vel=52)
        if name in ("C", "S", "S8"):
            parts["bells"] += arpeggio(spans, position, bars, 76, 95, step=0.25, order="updown", vel=48)
        if name == "C":
            parts["dxep"] += comp(spans, RHODES["C"], position, bars, 64, 86, count=3, vel=70)
        if rush and name != "intro":
            parts["arp"] += arpeggio(spans, position, bars, 62, 81, step=0.25, order="up", vel=72, gate=0.45)
        if name in ("A", "A2", "B", "C"):
            parts["lead"] += melody(MELODIES[name], position, vel=96)
        if name in ("S", "S8"):
            parts["solo"] += melody(MELODIES[name], position, vel=100)
        position = end
    intro = SECTION_BARS["intro"] * BAR
    body = position - intro

    hp = pb.HighpassFilter
    tracks = [
        Track("drums", "drums", humanize(parts["drums"], 1, 0.003, 6, swing=0.05), -17.0,
              [pb.Compressor(threshold_db=-16, ratio=2.5, attack_ms=8, release_ms=120),
               pb.HighShelfFilter(cutoff_frequency_hz=7000, gain_db=2.0)], hall=0.1),
        Track("bass", "bass", humanize(parts["bass"], 2, 0.004, 5, swing=0.05), -20.0,
              [hp(cutoff_frequency_hz=32), pb.Compressor(threshold_db=-22, ratio=4, attack_ms=5, release_ms=90),
               pb.LowShelfFilter(cutoff_frequency_hz=90, gain_db=2.0), pb.PeakFilter(cutoff_frequency_hz=260, gain_db=-2.5, q=1.0),
               pb.HighShelfFilter(cutoff_frequency_hz=2500, gain_db=2.0)], duck=2.5),
        Track("rhodes", "rhodes", humanize(parts["rhodes"], 3, 0.006, 6, swing=0.05), -23.0,
              [hp(cutoff_frequency_hz=110), pb.Chorus(rate_hz=0.6, depth=0.2, mix=0.35),
               pb.Phaser(rate_hz=0.4, depth=0.3, mix=0.2), pb.Compressor(threshold_db=-20, ratio=2.5, attack_ms=15, release_ms=150)],
              width=1.2, hall=0.25, delay=0.08),
        Track("pad", "surge:Pads/MKS-70 Warm Pad", parts["pad"], -27.0,
              [hp(cutoff_frequency_hz=180), pb.LowpassFilter(cutoff_frequency_hz=9000)], width=1.3, hall=0.45, duck=2.0),
        Track("strings", "surge:Polysynths/Juno-60 Strings", parts["strings"], -29.0, [hp(cutoff_frequency_hz=300)],
              width=1.2, hall=0.5),
        Track("bells", "surge:Plucks/Bell 2", humanize(parts["bells"], 4, 0.002, 6), -30.0, [hp(cutoff_frequency_hz=600)],
              pan=0.25, width=1.4, hall=0.4, delay=0.35),
        Track("brass", "surge:Brass/Toto Brass", humanize(parts["brass"], 5, 0.003, 4), -24.0,
              [hp(cutoff_frequency_hz=160), pb.Compressor(threshold_db=-18, ratio=3, attack_ms=10, release_ms=120)], hall=0.3),
        Track("lead", "surge:Leads/Moogy Saw", humanize(parts["lead"], 6, 0.003, 4), -19.5,
              [hp(cutoff_frequency_hz=200), pb.Compressor(threshold_db=-20, ratio=3, attack_ms=8, release_ms=120),
               pb.PeakFilter(cutoff_frequency_hz=3000, gain_db=1.5, q=0.8)], hall=0.28, delay=0.18, vibrato=0.18),
        Track("lead2", "surge:Leads/Smoothness World Cup", humanize(parts["lead"], 6, 0.003, 4), -26.0,
              [hp(cutoff_frequency_hz=200)], hall=0.3, vibrato=0.18),
        Track("solo", "surge:Leads/Synth Guitar 1", humanize(parts["solo"], 7, 0.003, 4), -20.0,
              [hp(cutoff_frequency_hz=200), pb.Compressor(threshold_db=-20, ratio=3, attack_ms=8, release_ms=120)],
              hall=0.3, delay=0.2, vibrato=0.22),
        Track("dxep", "surge:Keys/DX EP", humanize(parts["dxep"], 8, 0.004, 5), -28.0, [hp(cutoff_frequency_hz=400)],
              pan=-0.2, width=1.3, hall=0.35, delay=0.15),
    ]
    if rush:
        tracks.append(Track("arp", "surge:Plucks/The 1980s", humanize(parts["arp"], 9, 0.002, 5), -27.0,
                            [hp(cutoff_frequency_hz=300)], pan=-0.15, width=1.3, hall=0.25, delay=0.25, duck=2.0))
    tracks = [t for t in tracks if t.notes]
    return Song("athletic_rush" if rush else "athletic", 162 if rush else 150, intro, body, tracks,
                transpose=1 if rush else 0)
