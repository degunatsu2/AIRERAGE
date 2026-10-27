"""楽譜の書き方（音の高さ・長さ・強さ）。

時間の単位は拍（4 分音符 = 1.0）。旋律は文字列で書く:

    "r:2 b4:2 e5:2 f#5:2 g#5:4 b5:4 | -:6 a5:2 ..."

- `音名オクターブ:長さ`（長さは 16 分音符の数。省くと前と同じ）。C4 = 60。
- `r:長さ` は休符、`-:長さ` は前の音を延ばす（タイ）。
- 末尾の `!` は強く、`?` は弱く（ゴースト）。
- `[c5 e5 g5]:4` は和音。
- `|` は小節線。小節の頭でなければ例外にする（耳で確かめられないので、書き間違いをここで見つける）。
"""

from dataclasses import dataclass, field, replace
from typing import List, Optional

NOTE_OFFSETS = {"c": 0, "d": 2, "e": 4, "f": 5, "g": 7, "a": 9, "b": 11}
SIXTEENTH = 0.25


@dataclass
class Note:
    start: float  # 拍
    dur: float  # 拍
    pitch: int  # MIDI（C4 = 60）
    vel: int = 96
    art: Optional[str] = None  # 奏法（ドラムの部位、ベースのゴーストなど）
    extra: dict = field(default_factory=dict)

    def shifted(self, beats: float = 0.0, semitones: int = 0) -> "Note":
        return replace(self, start=self.start + beats, pitch=self.pitch + semitones)


def pitch(name: str) -> int:
    """'c#4' や 'eb3' を MIDI の番号にする。"""
    text = name.strip().lower()
    base = NOTE_OFFSETS[text[0]]
    i = 1
    while i < len(text) and text[i] in "#b":
        base += 1 if text[i] == "#" else -1
        i += 1
    octave = int(text[i:])
    return 12 * (octave + 1) + base


def _split_tokens(text: str) -> List[str]:
    tokens = []
    depth = 0
    current = ""
    for ch in text:
        if ch == "[":
            depth += 1
        elif ch == "]":
            depth -= 1
        if ch.isspace() and depth == 0:
            if current:
                tokens.append(current)
            current = ""
        else:
            current += ch
    if current:
        tokens.append(current)
    return tokens


def melody(text: str, start: float = 0.0, vel: int = 96, bar_beats: float = 4.0) -> List[Note]:
    """旋律の文字列を音の並びにする（start は書き始めの拍。小節線は start からの小節で調べる）。"""
    notes: List[Note] = []
    position = start
    dur16 = 4
    last: List[Note] = []
    for token in _split_tokens(text):
        if token == "|":
            offset = position - start
            if abs(offset / bar_beats - round(offset / bar_beats)) > 1e-6:
                raise ValueError(f"小節線が小節の頭にない: 拍 {offset}（{text[:60]}...）")
            continue
        accent = 0
        while token and token[-1] in "!?":
            accent += 1 if token[-1] == "!" else -1
            token = token[:-1]
        if ":" in token:
            head, length = token.rsplit(":", 1)
            dur16 = float(length)
        else:
            head = token
        dur = dur16 * SIXTEENTH
        if head == "r":
            last = []
        elif head == "-":
            if not last:
                raise ValueError(f"延ばす音がない: 拍 {position - start}")
            for n in last:
                n.dur += dur
        else:
            names = head[1:-1].split() if head.startswith("[") else [head]
            v = vel + 16 * accent if accent >= 0 else int(vel * 0.55)
            last = [Note(position, dur, pitch(n), max(1, min(127, v))) for n in names]
            notes.extend(last)
        position += dur
    return notes


def length_of(text: str) -> float:
    """旋律の文字列の長さ（拍）。"""
    total = 0.0
    dur16 = 4
    for token in _split_tokens(text):
        if token == "|":
            continue
        token = token.rstrip("!?")
        if ":" in token:
            dur16 = float(token.rsplit(":", 1)[1])
        total += dur16 * SIXTEENTH
    return total
