#!/bin/bash
set -euo pipefail

ARCH="docs/ARCHITECTURE.md"
DOCS_ENGINE="docs/ENGINE.md"
SCRATCH="${SCRATCH:-/tmp/grok-goal-84a82ec5c12d/implementer}"
mkdir -p "$SCRATCH"

REQUIRED_ARCH_SECTIONS=("Runtime flow" "Projects" "State" "Background work" "Query subsystem" "Security boundary")
REQUIRED_ENGINE_SECTIONS=("Invocation contract" "Explicit input boundary" "Capabilities" "State and confirmations" "Jobs and events")

echo "=== CURRENT ARCHITECTURE SECTIONS ===" > "$SCRATCH/contract-gate.txt"
FAILED=0
for section in "${REQUIRED_ARCH_SECTIONS[@]}"; do
  if grep -q "^## $section$" "$ARCH"; then
    echo "ARCH PASS: $section" >> "$SCRATCH/contract-gate.txt"
  else
    echo "ARCH MISSING: $section" >> "$SCRATCH/contract-gate.txt"
    FAILED=1
  fi
done
for section in "${REQUIRED_ENGINE_SECTIONS[@]}"; do
  if grep -q "^## $section$" "$DOCS_ENGINE"; then
    echo "ENGINE PASS: $section" >> "$SCRATCH/contract-gate.txt"
  else
    echo "ENGINE MISSING: $section" >> "$SCRATCH/contract-gate.txt"
    FAILED=1
  fi
done

# Marker: filter historical
echo "=== MARKER ===" >> "$SCRATCH/contract-gate.txt"
if grep -i -E 'open questions|sketch|todo|incomplete|will be refined' "$ARCH" "$DOCS_ENGINE" | grep -v -E 'Code example|Example registration|Definitions|resolved above' > /tmp/m.tmp 2>/dev/null && [ -s /tmp/m.tmp ]; then
  cat /tmp/m.tmp >> "$SCRATCH/contract-gate.txt"
  FAILED=1
else
  echo "CLEAN (historical ok)" >> "$SCRATCH/contract-gate.txt"
fi

if [ $FAILED -ne 0 ]; then
  echo "FAILED" | tee -a "$SCRATCH/contract-gate.txt"
  exit 1
fi
echo "PASSED" | tee -a "$SCRATCH/contract-gate.txt"
exit 0
