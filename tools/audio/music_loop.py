"""曲（Suno などで作った OGG）に、ループのタグを付ける（docs/18 6 章）。

/opt/airerage-audio/bin/python tools/audio/music_loop.py 入力.ogg 出力.ogg [--silence-db -80] [--keep-silence] [--keep-tags]
    [--crossfade-ms 30] [--quality 10]

曲の後ろが、前のどこかのくり返し（同じ波形がもう一度入っている）になっていることを前提にする:
1. 頭の無音（--silence-db より小さい音。初めは -80 dBFS。聞こえない大きさ）を切る。
2. 曲の最後の数秒と同じ波形の場所を探し、くり返しの周期（ループの長さ）をサンプルの単位で決める。
3. 1 周目と 2 周目の波形がそろっている範囲から、つなぎ目の前後の差がいちばん小さい所をループの始まりにし、そこから 1 周の所で
   曲を終える（それより後ろは鳴らないので切る）。
4. 終わりの 30ms を、ループの始まりの直前の音へ重ねて移す（書き出しの圧縮のせいで、2 周目は高い音が 1 周目と少し違う。
   つなぎ目で音が飛ばないように）。
5. OGG（Vorbis、品質 10。ゲームに読み込むときに Unity がもう一度圧縮するので、ここでは元の音にできるだけ近く）に書き出し、
   LOOPSTART と LOOPLENGTH（このファイルのサンプリング周波数の単位）を書く。
書き出したファイルを読み直して、長さ・タグ・つなぎ目の段差を確かめて表示する。

--keep-tags: 入力のファイルのループのタグ（LOOPSTART と LOOPLENGTH か LOOPEND）をそのまま使い、頭の無音を切った分だけ
LOOPSTART をずらす（ループの位置は変わらない。つなぎ目はそのまま。重ねない）。ループの終わりより後ろも残す。
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile

import numpy as np
import soundfile as sf

KEY_SECONDS = 3.0        # 曲の最後の、同じ波形を探す手がかりの長さ
MATCH_DB = -15.0         # 1 周目と 2 周目の差がこれより小さければ「そろっている」
WINDOW_SECONDS = 0.25    # そろっているかを調べる区切り
STEADY_SECONDS = 4.0     # これだけ続けてそろっていれば、そこからくり返しが始まっている
SEARCH_SECONDS = 10.0    # ループの始まりを探す範囲（くり返しが始まってから）


def decode(path):
    """音（float64、[サンプル, 2]）とサンプリング周波数。"""
    info = json.loads(subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=sample_rate", "-of", "json", path],
        capture_output=True, check=True).stdout)
    sr = int(info["streams"][0]["sample_rate"])
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).astype(np.float64), sr


def stream_tags(path):
    info = json.loads(subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream_tags", "-of", "json", path],
        capture_output=True, check=True).stdout)
    return {k.upper(): v for k, v in info["streams"][0].get("tags", {}).items()}


def clock(samples, sr):
    s = samples / sr
    return f"{int(s // 60)}:{s % 60:06.3f}"


def find_period(a, sr):
    """曲の最後の KEY_SECONDS と同じ波形の場所から、くり返しの周期（サンプル）を決める。"""
    n = len(a)
    d = 4
    mono = a.mean(axis=1)[::d]
    key_len = int(KEY_SECONDS * sr) // d
    key = mono[-key_len:]
    size = 1 << int(np.ceil(np.log2(len(mono) + key_len)))
    corr = np.fft.irfft(np.fft.rfft(mono, size) * np.conj(np.fft.rfft(key, size)), size)[:len(mono) - key_len]
    energy = np.concatenate([[0.0], np.cumsum(mono ** 2)])
    windowed = energy[key_len:key_len + len(corr)] - energy[:len(corr)]
    ncc = corr / (np.sqrt(windowed * np.dot(key, key)) + 1e-12)
    ncc[max(0, len(ncc) - int(5 * sr) // d):] = -1.0  # 手がかりの自分自身の近くは除く
    j = int(np.argmax(ncc))
    if ncc[j] < 0.8:
        sys.exit(f"曲の最後と同じ波形の場所が見つからない（いちばん近くて相関 {ncc[j]:.2f}）。くり返しのない曲は、"
                 "ループの位置を指定してもらう")
    coarse = (n - key_len * d) - j * d
    # サンプルの単位で詰める（曲の終わりの近くの 10 秒で、差がいちばん小さいずれ。探す幅の分だけ前から比べる）
    length = int(10 * sr)
    reach = 4 * d
    start = max(0, n - coarse - reach - length)
    ref = a[start:start + length]
    errors = [(np.mean((a[start + p:start + p + length] - ref) ** 2), p)
              for p in range(coarse - reach, coarse + reach + 1) if start + p + length <= n]
    return min(errors)[1], float(ncc[j])


def head_cut(a, silence_db):
    """頭の無音の終わり（silence_db より大きい音の最初のサンプル。-inf なら値が 0 でない最初のサンプル）。"""
    threshold = 0.0 if np.isinf(silence_db) else 10 ** (silence_db / 20)
    loud = np.nonzero(np.abs(a).max(axis=1) > threshold)[0]
    return int(loud[0]) if len(loud) else 0


def encode(y, sr, output, quality, loop_start, loop_length):
    with tempfile.TemporaryDirectory() as tmp:
        wav = os.path.join(tmp, "loop.wav")
        sf.write(wav, y.astype(np.float32), sr, subtype="FLOAT")
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", wav, "-map_metadata", "-1", "-c:a", "libvorbis",
                        "-q:a", str(quality), "-metadata:s:a:0", f"LOOPSTART={loop_start}",
                        "-metadata:s:a:0", f"LOOPLENGTH={loop_length}", output], check=True)


def verify(output, y, sr, loop_start, loop_length):
    """読み直して、長さとタグを確かめ、つなぎ目の段差と元の音との差を返す。"""
    b, sr_out = decode(output)
    tags = stream_tags(output)
    assert sr_out == sr and len(b) == len(y), (sr_out, len(b), len(y))
    assert tags.get("LOOPSTART") == str(loop_start) and tags.get("LOOPLENGTH") == str(loop_length), tags
    end = loop_start + loop_length
    assert end <= len(b), (end, len(b))
    seam = np.abs(b[loop_start] - b[end - 1]).max()
    steps = np.abs(np.diff(b, axis=0))
    codec = 10 * np.log10(np.mean((b - y) ** 2) / np.mean(y * y))
    return b, seam, steps, codec


def keep_tags(args, a, sr, cut):
    """入力のループのタグのまま、頭の無音を切る（LOOPSTART を切った分だけずらす）。"""
    tags = stream_tags(args.source)
    if "LOOPSTART" not in tags:
        sys.exit("入力のファイルにループのタグ（LOOPSTART）がない")
    start = int(tags["LOOPSTART"])
    end = start + int(tags["LOOPLENGTH"]) if "LOOPLENGTH" in tags else int(tags["LOOPEND"])
    if not (0 <= start < end <= len(a)):
        sys.exit(f"ループのタグが曲の中にない（始まり {start}、終わり {end}、曲の長さ {len(a)}）")
    if cut > start:
        sys.exit(f"頭の無音（{cut} サンプルまで）がループの始まり（{start}）より後ろまである")
    y = a[cut:].copy()
    loop_start, loop_length = start - cut, end - start
    encode(y, sr, args.output, args.quality, loop_start, loop_length)
    b, seam, steps, codec = verify(args.output, y, sr, loop_start, loop_length)
    print(f"元のファイル: {clock(len(a), sr)}（{sr}Hz、{len(a)} サンプル）。ループ {clock(start, sr)}〜{clock(end, sr)}")
    print(f"頭の無音: {clock(cut, sr)} まで（{cut} サンプル）を切った")
    print(f"ループ: 始まり {clock(loop_start, sr)}（LOOPSTART={loop_start}）、長さ {clock(loop_length, sr)}"
          f"（LOOPLENGTH={loop_length}）。終わり {clock(loop_start + loop_length, sr)}（曲の長さ {clock(len(b), sr)}）")
    print(f"書き出し: {args.output}（Vorbis 品質 {args.quality:g}。元の音との差 {codec:.1f} dB）")
    print(f"つなぎ目の段差 {seam:.4f}（曲の中の隣り合うサンプルの差: 中央値 {np.median(steps):.4f}、"
          f"99% {np.percentile(steps, 99):.4f}）")


def main() -> None:
    parser = argparse.ArgumentParser(description="曲にループのタグを付ける（くり返しの波形から探す）")
    parser.add_argument("source")
    parser.add_argument("output")
    parser.add_argument("--silence-db", type=float, default=-80.0,
                        help="頭の無音とみなす大きさ（dBFS。これより小さい音を切る。-inf なら値が 0 の所だけ）")
    parser.add_argument("--keep-silence", action="store_true", help="頭の無音を切らない")
    parser.add_argument("--keep-tags", action="store_true", help="入力のループのタグをそのまま使い、頭の無音だけを切る")
    parser.add_argument("--crossfade-ms", type=float, default=30.0)
    parser.add_argument("--quality", type=float, default=10.0, help="Vorbis の品質（-1〜10）")
    args = parser.parse_args()

    a, sr = decode(args.source)
    n = len(a)
    cut = 0 if args.keep_silence else head_cut(a, args.silence_db)
    if args.keep_tags:
        keep_tags(args, a, sr, cut)
        return
    period, ncc = find_period(a, sr)

    # 1 周目のどこから 2 周目とそろっているか
    w = int(WINDOW_SECONDS * sr)
    starts = list(range(cut, n - period - w, w))
    diff_db = []
    for i in starts:
        x = a[i:i + w]
        e = np.mean((a[i + period:i + period + w] - x) ** 2)
        diff_db.append(10 * np.log10(e / (np.mean(x * x) + 1e-20) + 1e-20))
    steady = int(STEADY_SECONDS / WINDOW_SECONDS)
    first = next((k for k in range(len(starts) - steady) if max(diff_db[k:k + steady]) < MATCH_DB), None)
    if first is None:
        sys.exit("1 周目と 2 周目の波形がそろっている所が見つからない")
    match_start = starts[first]

    # ループの始まり: そろっている所の中で、重ねる区間の差がいちばん小さい所
    x_len = int(args.crossfade_ms / 1000 * sr)
    lo = match_start + x_len
    hi = min(match_start + int(SEARCH_SECONDS * sr), n - period)
    candidates = [(np.mean((a[p + period - x_len:p + period] - a[p - x_len:p]) ** 2), p)
                  for p in range(lo, hi, sr // 100)]
    _, loop_start = min(candidates)
    end = loop_start + period

    y = a[cut:end].copy()
    fade = (0.5 - 0.5 * np.cos(np.linspace(0, np.pi, x_len)))[:, None]
    y[-x_len:] = (1 - fade) * a[end - x_len:end] + fade * a[loop_start - x_len:loop_start]
    tag_start = loop_start - cut
    encode(y, sr, args.output, args.quality, tag_start, period)
    b, seam, steps, codec = verify(args.output, y, sr, tag_start, period)
    assert tag_start + period == len(b)
    print(f"元のファイル: {clock(n, sr)}（{sr}Hz、{n} サンプル）")
    print(f"頭の無音: {clock(cut, sr)} まで（{cut} サンプル）を切った")
    print(f"くり返し: 1 周 {clock(period, sr)}（{period} サンプル。手がかりの相関 {ncc:.3f}）。"
          f"元のファイルの {clock(match_start, sr)} からの音が {clock(match_start + period, sr)} からもう一度入っている")
    print(f"ループ: 始まり {clock(tag_start, sr)}（LOOPSTART={tag_start}）、終わりは曲の終わり "
          f"{clock(len(b), sr)}（LOOPLENGTH={period}）。終わりの {args.crossfade_ms:.0f}ms を始まりの直前の音へ重ねた")
    print(f"書き出し: {args.output}（Vorbis 品質 {args.quality:g}。元の音との差 {codec:.1f} dB）")
    print(f"つなぎ目の段差 {seam:.4f}（曲の中の隣り合うサンプルの差: 中央値 {np.median(steps):.4f}、"
          f"99% {np.percentile(steps, 99):.4f}）")


if __name__ == "__main__":
    main()
