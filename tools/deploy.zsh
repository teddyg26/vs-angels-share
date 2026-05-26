#!/bin/zsh

set -e

# Default mod deployment directory.
# You can override this by passing a path as the first argument:
# ./tools/deploy.zsh "/some/other/path/AngelsShare_dev"
MOD_DIR="${1:-${VINTAGE_STORY_DATA:-$HOME/Library/Application Support/VintagestoryData}/Mods/AngelsShare_dev}"

# Resolve the directory this script lives in.
SCRIPT_DIR="${0:A:h}"

# Repo root is the parent of tools/
REPO_ROOT="${SCRIPT_DIR:h}"

echo "Repo root: $REPO_ROOT"
echo "Mod dir:   $MOD_DIR"

if [[ ! -d "$MOD_DIR" ]]; then
    echo "Creating mod directory..."
    mkdir -p "$MOD_DIR"
fi

echo "Deploying assets..."

# macOS equivalent of robocopy /MIR:
# -a preserves file info
# --delete removes files from destination that no longer exist in source
# trailing slashes are important here
rsync -a --delete "$REPO_ROOT/assets/" "$MOD_DIR/assets/"

echo "Deploying mod metadata..."
cp -f "$REPO_ROOT/modinfo.json" "$MOD_DIR/modinfo.json"

if [[ -f "$REPO_ROOT/modicon.png" ]]; then
    cp -f "$REPO_ROOT/modicon.png" "$MOD_DIR/modicon.png"
fi

echo "Building C# project..."
dotnet build "$REPO_ROOT/AngelsShare.csproj" -c Debug

echo "Deploy complete."
