#!/bin/bash
# Unity Editor（Linux 版）をダウンロードして展開する。クラウド環境で Unity をバッチモードで使うためのもの（docs/08_cloud_unity_setup.md）。
#
# 使い方:
#   bash tools/unity/install_editor.sh [バージョン]
#
# バージョンの決め方: 引数 → ProjectSettings/ProjectVersion.txt → 既定値（DEFAULT_VERSION）
# 展開先: ${UNITY_INSTALL_ROOT:-/opt/unity}/<バージョン>/Editor/Unity
# すでに展開済みなら何もしない。最後の行にエディタのパスを出力する。
#
# ダウンロード URL と MD5 は Unity のリリース API（services.api.unity.com）から取得する。
# 必要なネットワーク許可: *.unity3d.com、*.unity.com
set -euo pipefail

# Unity 6.3 LTS（D-01）。ProjectVersion.txt ができたら、そちらが優先される
DEFAULT_VERSION="6000.3.26f1"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
INSTALL_ROOT="${UNITY_INSTALL_ROOT:-/opt/unity}"

log() { echo "[install_editor] $*" >&2; }

version="${1:-}"
if [ -z "$version" ] && [ -f "$ROOT/ProjectSettings/ProjectVersion.txt" ]; then
  version="$(sed -n 's/^m_EditorVersion: *//p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
fi
version="${version:-$DEFAULT_VERSION}"

dest="$INSTALL_ROOT/$version"
editor="$dest/Editor/Unity"
if [ -x "$editor" ]; then
  log "展開済み: $editor"
  echo "$editor"
  exit 0
fi
if [ -e "$dest" ]; then
  log "$dest があるが Editor/Unity がない（前回の展開の残り？）。削除してから再実行してください"
  exit 1
fi

log "Unity $version のダウンロード情報を取得します"
api="https://services.api.unity.com/unity/editor/release/v1/releases?version=$version&platform=LINUX&architecture=X86_64&limit=25"
info="$(curl -fsS --retry 3 "$api" | python3 -I -c '
import base64, json, sys
version = sys.argv[1]
data = json.load(sys.stdin)
for release in data.get("results", []):
    if release.get("version") != version:
        continue
    for dl in release.get("downloads", []):
        if dl.get("platform") == "LINUX" and dl.get("architecture") == "X86_64" and dl.get("type") == "TAR_XZ":
            algo, _, digest = dl.get("integrity", "").partition("-")
            if algo != "md5":
                sys.exit("想定外の integrity の形式: " + dl.get("integrity", ""))
            print(dl["url"], base64.b64decode(digest).hex())
            sys.exit(0)
sys.exit("リリース API に Linux 版のダウンロードが見つからない: " + version)
' "$version")"
read -r url md5 <<<"$info"

# 展開は作業用のフォルダで行い、検査が通ってから最終的な場所へ移す（途中で失敗したときに壊れた状態を残さない）
mkdir -p "$INSTALL_ROOT"
work="$(mktemp -d "$INSTALL_ROOT/.install-$version.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkfifo "$work/md5.fifo"

# ダウンロードしながら展開する（順に行うより速い。セットアップスクリプトがキャッシュされる目安の約5分に収めるため）。
# アーカイブは複数ブロックで圧縮されているので、xz を並列に展開できる。MD5 は展開と並行して計算する。
# 途中で切れたときは、最初からやり直す（ストリームなので途中からの再開はできない）
log "ダウンロードしながら展開: $url"
start=$(date +%s)
for attempt in 1 2 3; do
  rm -rf "$work/extract"
  mkdir "$work/extract"
  md5sum <"$work/md5.fifo" >"$work/md5" &
  md5_pid=$!
  if curl -fsS "$url" | tee "$work/md5.fifo" | tar -I 'xz -T0' -x -C "$work/extract" --no-same-owner; then
    wait "$md5_pid"
    break
  fi
  wait "$md5_pid" || true
  if [ "$attempt" -eq 3 ]; then
    log "ダウンロードまたは展開に3回失敗した"
    exit 1
  fi
  log "ダウンロードまたは展開に失敗した（${attempt}回目）。やり直します"
  sleep $((attempt * 5))
done

actual="$(cut -d' ' -f1 "$work/md5")"
if [ "$actual" != "$md5" ]; then
  log "MD5 が一致しない（期待値 $md5、実際 $actual）"
  exit 1
fi
if [ ! -x "$work/extract/Editor/Unity" ]; then
  log "展開したアーカイブに Editor/Unity がない"
  exit 1
fi
mv "$work/extract" "$dest"
log "完了（$(( $(date +%s) - start ))秒、MD5 一致）: $editor"
echo "$editor"
