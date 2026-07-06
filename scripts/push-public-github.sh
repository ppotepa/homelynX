#!/usr/bin/env bash
set -euo pipefail

REPO_NAME="${1:-homelynx}"
REPO_OWNER="${GITHUB_OWNER:-ppotepa}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT"

if ! gh auth status >/dev/null 2>&1; then
  echo "GitHub CLI is not authenticated."
  echo "Run: gh auth login -h github.com -p https -w"
  echo "Or:  echo \"<GITHUB_TOKEN>\" | gh auth login --with-token"
  exit 1
fi

git branch -M main 2>/dev/null || true

if git remote get-url origin >/dev/null 2>&1; then
  echo "Remote origin already set: $(git remote get-url origin)"
else
  if gh repo view "${REPO_OWNER}/${REPO_NAME}" >/dev/null 2>&1; then
    echo "Linking existing repository ${REPO_OWNER}/${REPO_NAME}"
    gh repo set-visibility "${REPO_OWNER}/${REPO_NAME}" --public
    git remote add origin "https://github.com/${REPO_OWNER}/${REPO_NAME}.git"
  else
    echo "Creating public repository ${REPO_OWNER}/${REPO_NAME}"
    gh repo create "${REPO_NAME}" \
      --public \
      --source=. \
      --remote=origin \
      --description "Homelynx media automation control plane (.NET 8)"
  fi
fi

git push -u origin main
gh repo view "${REPO_OWNER}/${REPO_NAME}" --web 2>/dev/null || true

echo "Done: https://github.com/${REPO_OWNER}/${REPO_NAME}"