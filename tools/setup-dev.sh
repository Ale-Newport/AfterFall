#!/bin/sh
# One-time, repository-local developer setup for AFTERFALL.
# - Enables Git LFS for this repository only (does not touch global git config).
# - Installs the commit-msg hook that blocks AI attribution trailers.
set -eu
repo_root=$(git rev-parse --show-toplevel)
cd "$repo_root"

if command -v git-lfs >/dev/null 2>&1; then
  git lfs install --local
else
  echo "warning: git-lfs not found; install it (e.g. 'brew install git-lfs') and re-run." >&2
fi

hooks_dir=$(git rev-parse --git-path hooks)
mkdir -p "$hooks_dir"
cp tools/git-hooks/commit-msg "$hooks_dir/commit-msg"
chmod +x "$hooks_dir/commit-msg"
echo "Installed commit-msg hook into $hooks_dir"
