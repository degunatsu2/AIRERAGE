#!/bin/bash
# Unity の Personal ライセンスを、環境変数 UNITY_EMAIL / UNITY_PASSWORD を使って有効化する（docs/08_cloud_unity_setup.md 3.2）。
#
# 使い方:
#   bash tools/unity/activate_license.sh
#
# エディタは ${UNITY_INSTALL_ROOT:-/opt/unity}/<バージョン>/Editor/Unity にあるものを使う（tools/unity/install_editor.sh で導入）。
# すでに有効化済み（ライセンスファイルがある）なら何もしない。
# 認証に何度も失敗するとアカウントがロックされる可能性があるため、失敗しても再試行しない。
set -euo pipefail

ULF="$HOME/.local/share/unity3d/Unity/Unity_lic.ulf"
INSTALL_ROOT="${UNITY_INSTALL_ROOT:-/opt/unity}"

log() { echo "[activate_license] $*" >&2; }

if [ -f "$ULF" ]; then
  log "有効化済み: $ULF"
  exit 0
fi
if [ -z "${UNITY_EMAIL:-}" ] || [ -z "${UNITY_PASSWORD:-}" ]; then
  log "環境変数 UNITY_EMAIL と UNITY_PASSWORD が必要（docs/08 の 3.2）"
  exit 1
fi

client="$(ls -d "$INSTALL_ROOT"/*/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client 2>/dev/null | sort -V | tail -n 1)"
if [ -z "$client" ]; then
  log "ライセンスクライアントが見つからない。先に tools/unity/install_editor.sh でエディタを導入する"
  exit 1
fi

# 出力にメールアドレスが含まれるので伏せる（パスワードはクライアント自身が伏せ字にする）
out="$("$client" --activate-ulf --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" 2>&1)" && status=0 || status=$?
printf '%s\n' "${out//"$UNITY_EMAIL"/***}" >&2
if [ "$status" -ne 0 ] || [ ! -f "$ULF" ]; then
  log "有効化に失敗した（終了コード $status）。同じ値で再試行せず、docs/08 の 3.2 を確認する"
  exit 1
fi
log "有効化した: $ULF"
