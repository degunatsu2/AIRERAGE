"""シンセ（Surge XT・Dexed）を画面なしで鳴らす（DawDreamer）。

Surge XT の音色（.fxp）は DawDreamer の load_preset では読めないので、プラグインの状態（JUCE の VST3 の状態のファイル）の
中身を音色の中身に差し替えて load_state で読む。読んだ直後は音が出ないことがあるので、MIDI なしで少し鳴らしてから使う。
"""

import os
import re
import struct
import tempfile
from typing import List, Optional

import dawdreamer as daw
import mido
import numpy as np

from . import SAMPLE_RATE
from .notation import Note

SURGE = "/usr/lib/vst3/Surge XT.vst3"
SURGE_PATCHES = "/usr/share/surge-xt/patches_factory"
DEXED = os.environ.get("AIRERAGE_AUDIO_DIR", "/opt/airerage-audio") + "/plugins/Dexed.vst3"
BLOCK = 512
WARMUP = 0.5

_TABLE = ".ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+"
_INDEX = {c: i for i, c in enumerate(_TABLE)}


def _b64_decode(text: str) -> bytes:
    size_text, data = text.split(".", 1)
    size = int(size_text)
    value = 0
    for i, c in enumerate(data):
        value |= _INDEX[c] << (6 * i)
    return value.to_bytes(max(size, 1), "little")[:size]


def _b64_encode(raw: bytes) -> str:
    size = len(raw)
    value = int.from_bytes(raw, "little")
    chars = ((size << 3) + 5) // 6
    return f"{size}." + "".join(_TABLE[(value >> (6 * i)) & 63] for i in range(chars))


def _read_state(path: str) -> str:
    data = open(path, "rb").read()
    if data[:4] != b"VC2!":
        raise ValueError(f"JUCE の状態のファイルではない: {path}")
    return data[8:8 + struct.unpack("<I", data[4:8])[0]].decode("utf-8")


def _write_state(path: str, template_xml: str, component: bytes) -> None:
    xml = re.sub(r"<IComponent>[^<]*</IComponent>", "<IComponent>" + _b64_encode(component) + "</IComponent>",
                 template_xml)
    body = xml.encode("utf-8")
    with open(path, "wb") as f:
        f.write(b"VC2!" + struct.pack("<I", len(body)) + body)


def surge_patch(name: str) -> str:
    """'Leads/Classic Lead 1' のような名前から音色のファイルを探す。"""
    path = os.path.join(SURGE_PATCHES, name + ".fxp")
    if not os.path.exists(path):
        raise FileNotFoundError(f"Surge XT の音色がない: {name}")
    return path


LEAD_IN = 0.05  # 曲の頭の音の揺らぎが負の時刻にならないように、全体を少し遅らせて鳴らし、後で戻す
BEND_RANGE = 2.0  # ピッチベンドの幅（半音。Surge XT の初期値）


def _write_midi(path: str, notes: List[Note], bpm: float, vibrato: float) -> None:
    ticks_per_beat = 960
    seconds_per_tick = 60.0 / bpm / ticks_per_beat
    beat = 60.0 / bpm
    events = []  # (tick, 順番, メッセージ)

    def tick(t: float) -> int:
        return max(0, int(round((t + LEAD_IN) / seconds_per_tick)))

    for n in notes:
        start = n.start * beat + n.extra.get("offset", 0.0)
        end = start + max(0.01, n.dur * beat)
        pitch = int(n.pitch)
        events.append((tick(start), 1, mido.Message("note_on", note=pitch, velocity=int(max(1, min(127, n.vel))))))
        events.append((tick(end), 0, mido.Message("note_off", note=pitch, velocity=0)))
        if vibrato > 0 and end - start > 0.35:
            # 0.2 秒後から 0.3 秒かけて深くなる、5.5Hz のビブラート。
            t = start + 0.2
            while t < end - 0.02:
                depth = vibrato * min(1.0, (t - start - 0.2) / 0.3)
                value = int(8191 * depth / BEND_RANGE * np.sin(2 * np.pi * 5.5 * (t - start)))
                events.append((tick(t), 2, mido.Message("pitchwheel", pitch=max(-8192, min(8191, value)))))
                t += 0.015
            events.append((tick(end), 0, mido.Message("pitchwheel", pitch=0)))
    events.sort(key=lambda e: (e[0], e[1]))
    track = mido.MidiTrack()
    track.append(mido.MetaMessage("set_tempo", tempo=mido.bpm2tempo(bpm)))
    last = 0
    for t, _, message in events:
        track.append(message.copy(time=t - last))
        last = t
    midi = mido.MidiFile(ticks_per_beat=ticks_per_beat)
    midi.tracks.append(track)
    midi.save(path)


class Synth:
    """1 つの音色で音の並びを鳴らす。"""

    def __init__(self, patch: Optional[str] = None, plugin: str = SURGE, params: Optional[dict] = None):
        self.patch = patch
        self.plugin = plugin
        self.params = params or {}

    def render(self, notes: List[Note], bpm: float, seconds: float, vibrato: float = 0.0) -> np.ndarray:
        """notes を鳴らす。vibrato（半音）を渡すと、長い音に少し遅れてかかるビブラートをピッチベンドで付ける（単音の楽器用）。"""
        engine = daw.RenderEngine(SAMPLE_RATE, BLOCK)
        engine.set_bpm(bpm)
        synth = engine.make_plugin_processor("synth", self.plugin)
        if self.patch:
            raw = open(surge_patch(self.patch) if not os.path.isabs(self.patch) else self.patch, "rb").read()
            if raw[:4] != b"CcnK":
                raise ValueError(f"fxp ではない: {self.patch}")
            with tempfile.TemporaryDirectory() as tmp:
                template = os.path.join(tmp, "template.bin")
                state = os.path.join(tmp, "state.bin")
                synth.save_state(template)
                _write_state(state, _read_state(template), raw[60:])
                synth.load_state(state)
        if self.params:
            names = {p["name"]: p["index"] for p in synth.get_parameters_description()}
            for key, value in self.params.items():
                if key not in names:
                    raise KeyError(f"プラグインの設定項目がない: {key}")
                synth.set_parameter(names[key], value)
        engine.load_graph([(synth, [])])
        engine.render(WARMUP)  # 読んだ直後の無音を抜ける
        synth.clear_midi()
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "notes.mid")
            _write_midi(path, notes, bpm, vibrato)
            synth.load_midi(path, clear_previous=True, beats=False, all_events=True)
        engine.load_graph([(synth, [])])
        engine.render(seconds + LEAD_IN)
        audio = engine.get_audio().T.astype(np.float32)
        audio = audio[int(LEAD_IN * SAMPLE_RATE):]
        if audio.shape[1] == 1:
            audio = np.repeat(audio, 2, axis=1)
        return audio[:, :2]

    def describe(self) -> List[str]:
        engine = daw.RenderEngine(SAMPLE_RATE, BLOCK)
        synth = engine.make_plugin_processor("synth", self.plugin)
        return [f'{p["index"]}: {p["name"]}' for p in synth.get_parameters_description()]
