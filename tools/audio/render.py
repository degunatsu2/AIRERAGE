"""曲を書き出す: /opt/airerage-audio/bin/python tools/audio/render.py athletic [--rush] [--stems] [--out DIR]"""

import argparse
import importlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from airerage_audio.song import render  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser(description="AIRERAGE の BGM を書き出す")
    parser.add_argument("song", help="songs/ の中の曲の名前（例: athletic）")
    parser.add_argument("--rush", action="store_true", help="残り 1 分の版")
    parser.add_argument("--stems", action="store_true", help="楽器ごとの音も書き出す")
    parser.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "out"))
    args = parser.parse_args()
    module = importlib.import_module(f"songs.{args.song}")
    song = module.build(rush=args.rush)
    print(f"{song.name}: {song.bpm} BPM、前奏 {song.seconds(song.intro):.1f} 秒、本体 {song.seconds(song.body):.1f} 秒")
    for kind, path in render(song, args.out, stems=args.stems).items():
        print(f"  {kind}: {path}")


if __name__ == "__main__":
    main()
