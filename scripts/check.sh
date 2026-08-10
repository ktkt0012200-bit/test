#!/usr/bin/env bash
# ルールエンジンのテストと、サーバ経由の結合テストをまとめて実行する。
# Unity は不要。CI からもそのまま呼べる。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT/dotnet"

echo "== build =="
dotnet build SkullDive.sln -v q --nologo

echo
echo "== Unity 向けコードのコンパイル検証 =="
# Game/ は Unity 公式の参照アセンブリ (NuGet: UnityEngine.Modules) に対する本物の検証。
# Editor/ は UnityEditor の参照アセンブリが公開されていないため自前スタブに対する検証。
dotnet build SkullDive.UnityCheck -v q --nologo

echo
echo "== unit / soak tests =="
dotnet run --project SkullDive.Tests --nologo

echo
echo "== balance snapshot =="
dotnet run --project SkullDive.Cli --nologo -- --sim 2000

if [[ "${SKIP_NET_SMOKE:-0}" == "1" ]]; then
  echo
  echo "== net smoke: skipped (SKIP_NET_SMOKE=1) =="
  exit 0
fi

echo
echo "== net smoke (server + 2 clients + bots) =="
SkullDive__TurnTimeoutMs=1500 SkullDive__RoundEndDelayMs=400 \
  dotnet run --project SkullDive.Server --nologo > /tmp/skulldive-server.log 2>&1 &
SERVER_PID=$!
trap 'kill $SERVER_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 40); do
  if curl -sS --noproxy '*' -m 2 http://127.0.0.1:5099/healthz >/dev/null 2>&1; then break; fi
  sleep 1
done

dotnet run --project SkullDive.Cli --nologo -- --net-smoke --timeout 90
dotnet run --project SkullDive.Cli --nologo -- --net-smoke --idle-guest --timeout 90

echo
echo "all checks passed"
