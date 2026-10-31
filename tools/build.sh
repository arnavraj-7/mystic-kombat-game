#!/bin/sh
# Usage: tools/build.sh [Windows|Linux|Mac|All]   (batch-mode Unity build, prints errors)
T=${1:-Windows}
cd "$(dirname "$0")/.." || exit 1
"/c/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe" -batchmode -quit -projectPath "$(pwd -W 2>/dev/null || pwd)" \
  -executeMethod MysticKombat.EditorTools.ProjectSetup.Build$T -logFile Logs/build.log
code=$?
grep -E "error CS|\[build\]" Logs/build.log | sort -u
exit $code
