#!/bin/bash
# Unity の EditMode テストをバッチモードで実行し、結果を要約する（docs/08_cloud_unity_setup.md）。
#
# 使い方:
#   bash tools/unity/run_editmode_tests.sh
#
# エディタのバージョンは ProjectSettings/ProjectVersion.txt に合わせる（導入は tools/unity/install_editor.sh、
# ライセンスは tools/unity/activate_license.sh）。ログと結果の XML は Logs/（.gitignore の対象）に出力する。
# すべて通れば終了コード 0。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
INSTALL_ROOT="${UNITY_INSTALL_ROOT:-/opt/unity}"

version="$(sed -n 's/^m_EditorVersion: *//p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
editor="$INSTALL_ROOT/$version/Editor/Unity"
if [ ! -x "$editor" ]; then
  echo "[run_editmode_tests] Unity $version がない。bash tools/unity/install_editor.sh で導入する" >&2
  exit 1
fi

mkdir -p "$ROOT/Logs"
results="$ROOT/Logs/editmode-results.xml"
log="$ROOT/Logs/editmode.log"
rm -f "$results"

start=$(date +%s)
"$editor" -batchmode -nographics -projectPath "$ROOT" -runTests -testPlatform EditMode \
  -testResults "$results" -logFile "$log" && status=0 || status=$?
echo "[run_editmode_tests] Unity の終了コード $status（$(( $(date +%s) - start ))秒）。ログ: $log" >&2

grep -E "error CS[0-9]+" "$log" | sort -u >&2 || true
if [ ! -f "$results" ]; then
  echo "[run_editmode_tests] 結果の XML がない（コンパイルエラーかライセンスの問題の可能性。ログを確認する）" >&2
  exit 1
fi

python3 -I - "$results" <<'EOF'
import sys
import xml.etree.ElementTree as ET

run = ET.parse(sys.argv[1]).getroot()
print("結果: {} / 合計 {}、成功 {}、失敗 {}、スキップ {}".format(
    run.get("result"), run.get("total"), run.get("passed"), run.get("failed"), run.get("skipped")))
for case in run.iter("test-case"):
    if case.get("result") not in ("Passed", "Skipped"):
        message = case.findtext("failure/message", default="").strip()
        print("失敗: {}\n  {}".format(case.get("fullname"), message))
EOF
exit "$status"
