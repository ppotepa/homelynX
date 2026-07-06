#!/bin/bash
set -euo pipefail

ARCH="docs/ARCHITECTURE.md"
DOCS_ENGINE="docs/ENGINE.md"
SCRATCH="${SCRATCH:-/tmp/grok-goal-84a82ec5c12d/implementer}"
mkdir -p "$SCRATCH"

SEC9=$(grep -n '^## 9\.' "$ARCH" | head -1 | cut -d: -f1 || echo 0)
if [ "$SEC9" -eq 0 ]; then
  echo "ERROR: no ## 9" >&2
  exit 1
fi

TYPES=(IRequestContext IEngineContext IProcessManager IJobTracker Job JobOptions CapabilityContext ExecutionContext)

echo "SEC9=$SEC9" > "$SCRATCH/contract-gate.txt"
echo "=== COUNTS ===" >> "$SCRATCH/contract-gate.txt"
FAILED=0
for t in "${TYPES[@]}"; do
  cnt=$(grep -Ec "public (interface|record|class) $t([^A-Za-z0-9_]|$)" "$ARCH" || echo 0)
  echo "$t: $cnt" >> "$SCRATCH/contract-gate.txt"
  if [ "$cnt" -ne 1 ]; then FAILED=1; fi
  ln=$(grep -n "public (interface|record|class) $t([^A-Za-z0-9_]|$)" "$ARCH" | head -1 | cut -d: -f1 || echo 0)
  if [ "$ln" -gt 0 ] && [ "$ln" -lt "$SEC9" ]; then FAILED=1; fi
done
echo "JobOptions explicit: $(grep -c 'public record JobOptions' "$ARCH" || echo 0)" >> "$SCRATCH/contract-gate.txt"

# Marker: filter historical
echo "=== MARKER ===" >> "$SCRATCH/contract-gate.txt"
if grep -i -E 'open questions|sketch|todo|incomplete|will be refined' "$ARCH" "$DOCS_ENGINE" | grep -v -E 'Code example|Example registration|Definitions|resolved above' > /tmp/m.tmp 2>/dev/null && [ -s /tmp/m.tmp ]; then
  cat /tmp/m.tmp >> "$SCRATCH/contract-gate.txt"
  FAILED=1
else
  echo "CLEAN (historical ok)" >> "$SCRATCH/contract-gate.txt"
fi

echo "=== SEC11 ===" >> "$SCRATCH/contract-gate.txt"
tail -15 "$ARCH" | grep -A 25 '## 11\.' >> "$SCRATCH/contract-gate.txt" || true

if [ $FAILED -ne 0 ]; then
  echo "FAILED" | tee -a "$SCRATCH/contract-gate.txt"
  exit 1
fi
echo "PASSED" | tee -a "$SCRATCH/contract-gate.txt"
exit 0
