#!/usr/bin/env bash
# 技の性能表（docs/17_move_data.md）を、コードの値から作り直す（tools/CoreTests/Airerage.Core.Tests/Docs/MoveTable.cs）。
# 技の値（CharacterPresets・CombatTuning・SkillTuning）を変えたら実行する。作り直さないと dotnet test の MoveTableTests が失敗する。
set -euo pipefail
cd "$(dirname "$0")/.."
AIRERAGE_UPDATE_DOCS=1 dotnet test tools/CoreTests/Airerage.Core.Tests --filter "FullyQualifiedName~MoveTableTests" --nologo
echo "docs/17_move_data.md を作り直した"
