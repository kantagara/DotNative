#!/usr/bin/env bash
set -u
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="${FNI_TEST_ARTIFACTS:-/tmp/fni11-tests}"
logs="${FNI_TEST_LOGS:-/tmp/fni11-evidence}"
mkdir -p "$logs"
cd "$root" || exit 1
failed=0
for framework in net10.0 net9.0; do
  for project in tests/FniFeature*.Tests tests/FaliNamIgrac.Tests; do
    name="${project##*/}"
    log="$logs/$name-$framework.log"
    if dotnet build "$project" -f "$framework" --artifacts-path "$output" -m:1 -v:q > "$log" 2>&1 &&
       DOTNET_ROLL_FORWARD=Major dotnet "$output/bin/$name/debug_$framework/$name.dll" >> "$log" 2>&1; then
      echo "PASS $name $framework"
    else
      echo "FAIL $name $framework ($log)"
      failed=1
    fi
  done
done
exit "$failed"
