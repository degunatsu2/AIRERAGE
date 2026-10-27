"""効果音を書き出す: /opt/airerage-audio/bin/python tools/audio/render_se.py [名前の先頭 ...] [--out DIR]"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from airerage_audio import mix  # noqa: E402
from sounds.menu_se import SOUNDS  # noqa: E402


def main() -> None:
    parser = argparse.ArgumentParser(description="AIRERAGE の効果音を書き出す")
    parser.add_argument("names", nargs="*", help="書き出す音の名前の先頭（省くと全部）")
    parser.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "se"))
    args = parser.parse_args()
    for name, build in SOUNDS.items():
        if args.names and not any(name.startswith(n) for n in args.names):
            continue
        a = build()
        path = os.path.join(args.out, name + ".wav")
        mix.write(path, a)
        print(f"{path}: {len(a) / 48000:.2f} 秒、ピーク {20 * __import__('numpy').log10(abs(a).max()):.1f} dB", flush=True)


if __name__ == "__main__":
    main()
