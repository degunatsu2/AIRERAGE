#!/bin/bash
# クラウドセッション（Claude Code on the web）の開始時に、Core のテストに必要な .NET 8 SDK を導入する。
# Microsoft の配布元はネットワークポリシーで遮断されているため、Ubuntu のリポジトリから入れる。
# ローカル環境では何もしない。
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"

if ! dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  echo "[session-start] .NET 8 SDK をインストールします" >&2
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0 >/dev/null
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi

echo "[session-start] NuGet パッケージを復元します" >&2
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
  dotnet restore "$PROJECT_DIR/tools/CoreTests/Degunatsu.Core.Tests" --verbosity quiet >&2
