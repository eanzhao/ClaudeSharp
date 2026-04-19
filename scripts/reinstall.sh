#!/usr/bin/env bash
# Rebuild Aexon from source and (re)install it as a global .NET tool.
#
# Usage:
#   scripts/reinstall.sh                # pack + reinstall global tool
#   scripts/reinstall.sh --local        # install into ./.tools instead of globally
#   scripts/reinstall.sh --configuration Debug

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

CONFIGURATION="Release"
INSTALL_MODE="global"
TOOL_PATH="$REPO_ROOT/.tools"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --local)
      INSTALL_MODE="local"
      shift
      ;;
    --tool-path)
      INSTALL_MODE="local"
      TOOL_PATH="$2"
      shift 2
      ;;
    --configuration|-c)
      CONFIGURATION="$2"
      shift 2
      ;;
    -h|--help)
      sed -n '2,8p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

PACKAGE_ID="Aexon"
CLI_PROJECT="$REPO_ROOT/src/Aexon.Cli/Aexon.Cli.csproj"
PACKAGE_OUTPUT="$REPO_ROOT/artifacts/packages"

echo "==> Building Aexon.Commands frontend (chat + workbench)..."
FRONTEND_DIR="$REPO_ROOT/src/Aexon.Commands/Frontend"
if [[ ! -f "$FRONTEND_DIR/package.json" ]]; then
  echo "Frontend directory not found at $FRONTEND_DIR" >&2
  exit 1
fi

if ! command -v npm >/dev/null 2>&1; then
  echo "npm not found on PATH. Install Node.js (>= 20) to build the frontend." >&2
  exit 1
fi

(
  cd "$FRONTEND_DIR"
  if [[ -f package-lock.json ]]; then
    npm ci --ignore-scripts
  else
    npm install --ignore-scripts
  fi
  npm run build
)
echo "==> Frontend build complete."

echo "==> Cleaning previous package output"
rm -rf "$PACKAGE_OUTPUT"
mkdir -p "$PACKAGE_OUTPUT"

echo "==> Restoring solution"
dotnet restore "$REPO_ROOT/Aexon.slnx"

echo "==> Packing $PACKAGE_ID ($CONFIGURATION)"
dotnet pack "$CLI_PROJECT" \
  --configuration "$CONFIGURATION" \
  --output "$PACKAGE_OUTPUT"

PACKAGE_FILE="$(ls -t "$PACKAGE_OUTPUT"/${PACKAGE_ID}.*.nupkg | head -n1)"
if [[ -z "$PACKAGE_FILE" ]]; then
  echo "No .nupkg produced in $PACKAGE_OUTPUT" >&2
  exit 1
fi
PACKAGE_VERSION="$(basename "$PACKAGE_FILE" | sed -E "s/^${PACKAGE_ID}\.(.+)\.nupkg$/\1/")"
echo "==> Built $PACKAGE_ID $PACKAGE_VERSION"

if [[ "$INSTALL_MODE" == "global" ]]; then
  echo "==> Uninstalling existing global tool (if present)"
  dotnet tool uninstall --global "$PACKAGE_ID" 2>/dev/null || true

  echo "==> Installing $PACKAGE_ID $PACKAGE_VERSION globally from $PACKAGE_OUTPUT"
  dotnet tool install --global "$PACKAGE_ID" \
    --version "$PACKAGE_VERSION" \
    --add-source "$PACKAGE_OUTPUT"

  echo "==> Done. Run 'aexon --help' to verify."
else
  echo "==> Uninstalling existing tool at $TOOL_PATH (if present)"
  dotnet tool uninstall --tool-path "$TOOL_PATH" "$PACKAGE_ID" 2>/dev/null || true

  echo "==> Installing $PACKAGE_ID $PACKAGE_VERSION into $TOOL_PATH"
  mkdir -p "$TOOL_PATH"
  dotnet tool install --tool-path "$TOOL_PATH" "$PACKAGE_ID" \
    --version "$PACKAGE_VERSION" \
    --add-source "$PACKAGE_OUTPUT"

  echo "==> Done. Run '$TOOL_PATH/aexon --help' to verify."
fi
