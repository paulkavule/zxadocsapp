#!/usr/bin/env bash
# Verifies that templates and drafts still render correctly: images and tables display, images can
# be resized and positioned, and the generated PDF keeps their position and dimensions.
#
# Three tiers, cheapest first:
#   fast  build + assertions that need no browser and no server (~10s)
#   ui    Playwright against the running app: editor + the four preview surfaces (~1m)
#   pdf   a real LibreOffice conversion, measured (~10s)
#
#   ./scripts/verify-render.sh              all three
#   ./scripts/verify-render.sh fast         the per-edit pass only
#   ./scripts/verify-render.sh ui pdf       skip the fast tier
#
# The ui tier needs the API and UI running. Pass --start to have this script launch them, or start
# them yourself with `dotnet run --launch-profile https` in ../DocsApi/zxadocsapi and in zxadocsui.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
docuapp="$(cd "$here/.." && pwd)"
docsapi="$(cd "$docuapp/../DocsApi" && pwd)"
lib="$(cd "$docuapp/../zxadocslib" && pwd)"

ui_url="https://localhost:7086"
api_url="https://localhost:7028/swagger/index.html"
log_dir="${TMPDIR:-/tmp}/zxadocs-verify"
start_apps=0
tiers=()

for arg in "$@"; do
  case "$arg" in
    --start) start_apps=1 ;;
    fast|ui|pdf) tiers+=("$arg") ;;
    -h|--help) sed -n '2,18p' "$0"; exit 0 ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done
[ ${#tiers[@]} -eq 0 ] && tiers=(fast ui pdf)

has_tier() { for t in "${tiers[@]}"; do [ "$t" = "$1" ] && return 0; done; return 1; }
up() { curl -sk -o /dev/null --max-time 5 "$1"; }

failures=0
step() {  # step <label> <command...>
  local label="$1"; shift
  printf '\n=== %s\n' "$label"
  if "$@"; then
    printf '    PASS  %s\n' "$label"
  else
    printf '    FAIL  %s\n' "$label"
    failures=$((failures + 1))
  fi
}

# ---------------------------------------------------------------- fast
if has_tier fast; then
  step "build (shared contracts, app, tests)" \
    dotnet build "$docuapp/DocsApp.sln" -v q --nologo
  step "shared contract assertions (zxadocslib)" \
    dotnet test "$lib/zxadocslib.sln" --nologo -v q
  step "stored-document, normalizer + signer-lifetime assertions (DocsApi, no LibreOffice)" \
    dotnet test "$docsapi/zxadocsapi.Tests/zxadocsapi.Tests.csproj" --nologo -v q --filter "Category!=Pdf"
  step "dead-style guard (no browser)" \
    dotnet test "$docuapp/zxadocsui.Tests/zxadocsui.Tests.csproj" --nologo -v q --filter "Category=Fast"
fi

# ---------------------------------------------------------------- ui
if has_tier ui; then
  if ! up "$ui_url" || ! up "$api_url"; then
    if [ "$start_apps" = "1" ]; then
      mkdir -p "$log_dir"
      echo "starting API and UI (logs in $log_dir)"
      ( cd "$docsapi/zxadocsapi" && nohup dotnet run --launch-profile https >"$log_dir/api.log" 2>&1 & )
      ( cd "$docuapp/zxadocsui" && nohup dotnet run --launch-profile https >"$log_dir/ui.log" 2>&1 & )
      for _ in $(seq 1 40); do
        up "$ui_url" && up "$api_url" && break
        sleep 3
      done
    fi
  fi

  if up "$ui_url" && up "$api_url"; then
    step "editor + preview surfaces in a real browser (Playwright)" \
      dotnet test "$docuapp/zxadocsui.Tests/zxadocsui.Tests.csproj" --nologo -v q --filter "Category=Ui"
  else
    printf '\n=== editor + preview surfaces in a real browser (Playwright)\n'
    printf '    SKIP  the app is not running. Re-run with --start, or start it yourself:\n'
    printf '            (cd %s/zxadocsapi && dotnet run --launch-profile https)\n' "$docsapi"
    printf '            (cd %s/zxadocsui  && dotnet run --launch-profile https)\n' "$docuapp"
    failures=$((failures + 1))
  fi
fi

# ---------------------------------------------------------------- pdf
if has_tier pdf; then
  if [ -x /Applications/LibreOffice.app/Contents/MacOS/soffice ] || command -v soffice >/dev/null 2>&1; then
    step "generated PDF geometry (real LibreOffice conversion)" \
      dotnet test "$docsapi/zxadocsapi.Tests/zxadocsapi.Tests.csproj" --nologo -v q --filter "Category=Pdf"
  else
    printf '\n=== generated PDF geometry (real LibreOffice conversion)\n'
    printf '    SKIP  LibreOffice not found; the PDF assertions cannot run.\n'
    failures=$((failures + 1))
  fi
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
  echo "render verification passed (${tiers[*]})"
else
  echo "render verification FAILED: $failures step(s) — see above"
fi
exit "$failures"
