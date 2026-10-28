#!/usr/bin/env bash
# Builds PhotinoNETUtils and copies its DLL and npm tarball into the configured projects.
#
# Create build-and-copy.config.json (git-ignored) from build-and-copy.config.example.json:
#
#   {
#     "libsDirectory": "Libs",
#     "userInterfaceDirectory": "UserInterface",
#     "targets": [
#       { "name": "MyApp", "path": "../MyApp/MyApp" }
#     ]
#   }
#
# Each target gets:
#
#   <target>/<libsDirectory>/PhotinoNETUtils.dll
#   <target>/<libsDirectory>/photino-net-utils-bridge-<version>.tgz
#
# The "@photino-net-utils/bridge" dependency in <target>/<userInterfaceDirectory>/package.json
# is updated to point at that tarball, unless that file does not exist.
#
# Usage:
#   ./build-and-copy.sh [--config FILE] [--skip-npm] [--skip-target-install]

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="$ROOT/build-and-copy.config.json"
SKIP_NPM=0
SKIP_TARGET_INSTALL=0

usage() {
    sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --config)
            CONFIG="$2"
            shift 2
            ;;
        --skip-npm)
            SKIP_NPM=1
            shift
            ;;
        --skip-target-install)
            SKIP_TARGET_INSTALL=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

PROJECT="$ROOT/PhotinoNETUtils/PhotinoNETUtils.csproj"
UI="$ROOT/UserInterface"
NPM_MANIFEST="$UI/package.json"
READ_CONFIG="$ROOT/scripts/read-config.mjs"
SET_DEPENDENCY="$ROOT/scripts/set-bridge-dependency.mjs"

[[ -f "$PROJECT" ]] || { echo "Project not found: $PROJECT" >&2; exit 1; }
[[ -f "$NPM_MANIFEST" ]] || { echo "npm package not found: $UI" >&2; exit 1; }
[[ -f "$CONFIG" ]] || {
    echo "Config not found: $CONFIG" >&2
    echo "Copy build-and-copy.config.example.json to build-and-copy.config.json and edit it." >&2
    exit 1
}

LIBRARY_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)"
NPM_VERSION="$(node -p "require('$NPM_MANIFEST').version")"

if [[ -z "$LIBRARY_VERSION" ]]; then
    echo "Could not read <Version> from $PROJECT" >&2
    exit 1
fi

if [[ "$LIBRARY_VERSION" != "$NPM_VERSION" ]]; then
    echo "Warning: version mismatch: library $LIBRARY_VERSION, npm package $NPM_VERSION" >&2
fi

echo "Building PhotinoNETUtils $LIBRARY_VERSION (Release)..."
dotnet build "$PROJECT" -c Release --nologo

DLL="$ROOT/PhotinoNETUtils/bin/Release/net10.0/PhotinoNETUtils.dll"
[[ -f "$DLL" ]] || { echo "DLL not found: $DLL" >&2; exit 1; }

TGZ_NAME="photino-net-utils-bridge-$NPM_VERSION.tgz"
TGZ="$UI/$TGZ_NAME"

if [[ "$SKIP_NPM" -eq 0 ]]; then
    if [[ ! -d "$UI/node_modules" ]]; then
        echo "Installing the npm package dependencies..."
        (cd "$UI" && npm install --no-audit --no-fund)
    fi

    echo "Packing $TGZ_NAME..."
    (cd "$UI" && npm run pack)
fi

[[ -f "$TGZ" ]] || { echo "npm tarball not found: $TGZ" >&2; exit 1; }

while IFS=$'\t' read -r NAME TARGET LIBS UI_DIR; do
    [[ -n "$NAME" ]] || continue

    if [[ ! -d "$TARGET" ]]; then
        echo "Skipping $NAME: $TARGET does not exist" >&2
        continue
    fi

    LIB_DIR="$TARGET/$LIBS"
    mkdir -p "$LIB_DIR"

    cp -f "$DLL" "$LIB_DIR/PhotinoNETUtils.dll"
    find "$LIB_DIR" -maxdepth 1 -type f -name 'photino-net-utils-bridge-*.tgz' \
        ! -name "$TGZ_NAME" -delete
    cp -f "$TGZ" "$LIB_DIR/$TGZ_NAME"

    APP_UI="$TARGET/$UI_DIR"
    APP_MANIFEST="$APP_UI/package.json"

    if [[ -f "$APP_MANIFEST" ]]; then
        node "$SET_DEPENDENCY" "$APP_MANIFEST" "$LIB_DIR" "$TGZ_NAME"

        if [[ "$SKIP_TARGET_INSTALL" -eq 0 ]]; then
            rm -rf "$APP_UI/node_modules/@photino-net-utils/bridge"
            echo "Installing the shared bridge in $APP_UI..."
            (cd "$APP_UI" && npm install --no-audit --no-fund)
        fi
    fi

    echo "Copied to $LIB_DIR"
done < <(node "$READ_CONFIG" "$CONFIG")

echo "Done."
