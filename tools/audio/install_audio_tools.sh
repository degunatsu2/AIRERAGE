#!/bin/bash
# BGM と SE の制作に使う道具を導入する（Linux。クラウドのセッションで使う。tools/audio/README.md）。
# すでに入っているものは飛ばすので、何度実行してもよい。音源（サンプル）は合わせて約 5GB。
#
# - シンセ: Surge XT（VST3、GPL）と Dexed（DX7 の再現、VST3、GPL）。リバーブ: Dragonfly Reverb（VST3、GPL）
# - 音源: sfzinstruments の 4 つ（ドラム 2 つ・ベースは CC0、ローズは「音楽を作るのに使うのは自由」）。曲の書き出しにだけ使い、音源そのものは配布しない
# - Python: DawDreamer（プラグインを画面なしで鳴らす）、pedalboard（エフェクト）、pyloudnorm（ラウドネス）ほか
set -euo pipefail

AUDIO_DIR="${AIRERAGE_AUDIO_DIR:-/opt/airerage-audio}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DOWNLOADS="$AUDIO_DIR/downloads"
PLUGINS="$AUDIO_DIR/plugins"
SAMPLES="$AUDIO_DIR/samples"
mkdir -p "$DOWNLOADS" "$PLUGINS" "$SAMPLES"

log() { echo "[audio] $*" >&2; }

# ファイルを取得して、SHA-256 が合うか確かめる（合わなければ止める）。
fetch() {
  local url="$1" out="$2" sha="$3"
  if [ ! -f "$out" ] || ! echo "$sha  $out" | sha256sum -c --status; then
    log "取得: $url"
    curl -fsSL --retry 3 -o "$out.part" "$url"
    mv "$out.part" "$out"
  fi
  echo "$sha  $out" | sha256sum -c --status || { log "SHA-256 が合わない: $out"; exit 1; }
}

export DEBIAN_FRONTEND=noninteractive
if ! command -v sox >/dev/null 2>&1 || [ ! -d /usr/lib/vst3/DragonflyHallReverb.vst3 ]; then
  log "apt: sox, ffmpeg, Dragonfly Reverb"
  apt-get update -qq
  apt-get install -y -qq sox ffmpeg dragonfly-reverb-vst3 >/dev/null
fi

if [ ! -d "/usr/lib/vst3/Surge XT.vst3" ]; then
  fetch https://github.com/surge-synthesizer/releases-xt/releases/download/1.3.4/surge-xt-linux-x64-1.3.4.deb \
    "$DOWNLOADS/surge-xt.deb" a6e55064487f624147d515b9ae5fc79a568b69746675b2083abde628ca7bb151
  log "Surge XT 1.3.4"
  apt-get install -y -qq "$DOWNLOADS/surge-xt.deb" >/dev/null
fi

if [ ! -d "$PLUGINS/Dexed.vst3" ]; then
  fetch https://github.com/asb2m10/dexed/releases/download/v0.9.8/dexed-0.9.8-lnx.zip \
    "$DOWNLOADS/dexed.zip" 5d026f53504f9303ae2a4a635cf6fdfc50ab9c947cbc0a20ecb5c8f323402dab
  log "Dexed 0.9.8"
  rm -rf "$DOWNLOADS/dexed"
  mkdir -p "$DOWNLOADS/dexed"
  (cd "$DOWNLOADS/dexed" && unzip -q ../dexed.zip)
  cp -r "$(find "$DOWNLOADS/dexed" -type d -name 'Dexed.vst3' | head -1)" "$PLUGINS/"
fi

if [ ! -x "$AUDIO_DIR/bin/python" ]; then
  log "Python の仮想環境: $AUDIO_DIR"
  python3 -m venv "$AUDIO_DIR"
fi
if ! "$AUDIO_DIR/bin/pip" freeze 2>/dev/null | grep -q '^soxr=='; then
  log "Python のパッケージ"
  "$AUDIO_DIR/bin/pip" install -q -r "$SCRIPT_DIR/requirements.txt"
fi

# 音源。コミットを固定して、浅く取得する。4 つ目以降の引数を渡すと、そのフォルダだけを取る（sparse checkout）。
clone() {
  local name="$1" url="$2" commit="$3"
  shift 3
  if [ "$(git -C "$SAMPLES/$name" rev-parse HEAD 2>/dev/null || true)" = "$commit" ]; then
    return
  fi
  log "音源: $name"
  rm -rf "$SAMPLES/$name"
  git init -q "$SAMPLES/$name"
  git -C "$SAMPLES/$name" config gc.auto 0
  git -C "$SAMPLES/$name" remote add origin "$url"
  if [ "$#" -gt 0 ]; then
    git -C "$SAMPLES/$name" sparse-checkout init --no-cone
    printf '%s\n' "$@" > "$SAMPLES/$name/.git/info/sparse-checkout"
    git -C "$SAMPLES/$name" fetch -q --depth 1 --filter=blob:none origin "$commit"
  else
    git -C "$SAMPLES/$name" fetch -q --depth 1 origin "$commit"
  fi
  git -C "$SAMPLES/$name" checkout -q FETCH_HEAD
}
clone virtuosity_drums https://github.com/sfzinstruments/virtuosity_drums 9f04cf9a734527edfbb0a4eee1f674e45bbf71bc
clone black_and_blue_basses https://github.com/sfzinstruments/karoryfer.black-and-blue-basses 6e7d674cdb41be7a54dbccb15472401ad01099b9
clone jRhodes3d https://github.com/sfzinstruments/jlearman.jRhodes3d aea5b8d3e11e2f7102593789a4e0a0e41b30271a
# シンバルとハイハットだけ（明るい音。CC0）
clone unruly_drums https://github.com/sfzinstruments/karoryfer.unruly-drums 9bf75c2a1392f190cd1c264645653629f0b3a097 \
  /LICENSE /Samples/c16/ /Samples/h14/ /Samples/r20/

log "完了: $AUDIO_DIR（曲の書き出しは tools/audio/README.md）"
