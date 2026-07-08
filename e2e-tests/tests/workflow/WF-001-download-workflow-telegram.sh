#!/bin/bash
# WF-001: Podstawowy workflow pobierania przez Telegram test endpoint
# Search → Start (z confirmation) → Status → Pause → Resume → Cancel

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../../helpers/dual-path.sh"

TEST_ID="WF-001"
TEST_NAME="Podstawowy workflow pobierania (Telegram)"

log_test_start "$TEST_ID" "$TEST_NAME"

# Cleanup: Usuń wszystkie torrenty z qBittorrent żeby uniknąć konfliktów
log_step "Cleanup: Usuwanie torrentów z qBittorrent"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
set -a && source "$SCRIPT_DIR/../../../.env" && set +a
if [[ -n "$QBIT_HOST" ]]; then
    QBIT_PORT="${QBIT_PORT:-8080}"
    QBIT_SCHEME="http"
    [[ "$QBIT_HTTPS" == "true" ]] && QBIT_SCHEME="https"
    QBIT_URL="$QBIT_SCHEME://localhost:$QBIT_PORT"
elif [[ -n "$QBITTORRENT_URL" ]]; then
    QBIT_URL="$QBITTORRENT_URL"
else
    QBIT_URL="http://localhost:8080"
fi

QBIT_USER="${QBIT_USERNAME:-${QBITTORRENT_USER:-admin}}"
QBIT_PASS="${QBIT_PASSWORD:-$QBITTORRENT_PASS}"
COOKIE_FILE="/tmp/qbit-cookies-$$"
curl -s -c "$COOKIE_FILE" -X POST "$QBIT_URL/api/v2/auth/login" -d "username=$QBIT_USER&password=$QBIT_PASS" >/dev/null 2>&1
HASHES=$(curl -s -b "$COOKIE_FILE" "$QBIT_URL/api/v2/torrents/info" 2>/dev/null | jq -r '.[].hash' 2>/dev/null)
for hash in $HASHES; do
    curl -s -b "$COOKIE_FILE" -X POST "$QBIT_URL/api/v2/torrents/delete" -d "hashes=$hash&deleteFiles=false" >/dev/null 2>&1
done
rm -f "$COOKIE_FILE"
log_info "Usunięto torrenty z qBittorrent"

# Krok 1: Search
log_step "Krok 1: Wyszukiwanie torrentów (ubuntu)"
SEARCH_RESULT=$(send_telegram_command "/download_search ubuntu")
assert_not_empty "$SEARCH_RESULT" "Wynik wyszukiwania nie jest pusty" || exit_test "$TEST_ID" "FAIL" "Brak wyniku"

# Sprawdź czy są wyniki
if echo "$SEARCH_RESULT" | grep -q "Found.*torrent"; then
    log_info "Znaleziono torrenty"
else
    log_warning "Nie znaleziono torrentów lub inny format odpowiedzi"
fi

# Krok 2: Start download (wymaga confirmation)
log_step "Krok 2: Rozpoczęcie pobierania (pierwszy wynik) - wymaga potwierdzenia"
START_RESULT=$(send_telegram_command "/download 0")

# Sprawdź czy wymaga potwierdzenia
if echo "$START_RESULT" | grep -qi "confirmation\|confirm"; then
    log_info "Wymagane potwierdzenie"
    
    # Wyciągnij token z odpowiedzi (jeśli jest w formacie "Token: xxx")
    TOKEN=$(echo "$START_RESULT" | grep -oP 'Token:\s*\K[a-zA-Z0-9]+' || echo "")
    
    if [ -n "$TOKEN" ]; then
        log_info "Confirmation token: $TOKEN"
        
        # Potwierdź
        CONFIRM_RESULT=$(send_telegram_command "/confirm $TOKEN")
        log_info "Potwierdzenie: $CONFIRM_RESULT"
    else
        log_warning "Nie znaleziono tokenu potwierdzenia"
    fi
else
    log_info "Download rozpoczęty bez potwierdzenia"
fi

# Krok 3: Check status przez /downloads
log_step "Krok 3: Sprawdzenie statusu przez /downloads"
sleep 3
LIST_RESULT=$(send_telegram_command "/downloads")
assert_not_empty "$LIST_RESULT" "Wynik list nie jest pusty" || exit_test "$TEST_ID" "FAIL" "Brak wyniku"

if echo "$LIST_RESULT" | grep -q "download"; then
    log_info "Znaleziono downloady"
else
    log_warning "Nie znaleziono downloadów lub inny format odpowiedzi"
fi

# Krok 4: Pauza
log_step "Krok 4: Pauzowanie downloadu"
PAUSE_RESULT=$(send_telegram_command "/pause 0")
log_info "Pauza: $PAUSE_RESULT"

# Krok 5: Wznowienie
log_step "Krok 5: Wznawianie downloadu"
RESUME_RESULT=$(send_telegram_command "/resume 0")
log_info "Wznowienie: $RESUME_RESULT"

# Krok 6: Anulowanie
log_step "Krok 6: Anulowanie downloadu"
CANCEL_RESULT=$(send_telegram_command "/cancel 0")
log_info "Anulowanie: $CANCEL_RESULT"

# Sprawdź czy wymaga potwierdzenia
if echo "$CANCEL_RESULT" | grep -qi "confirmation\|confirm"; then
    log_info "Anulowanie wymaga potwierdzenia"
    TOKEN=$(echo "$CANCEL_RESULT" | grep -oP 'Token:\s*\K[a-zA-Z0-9]+' || echo "")
    
    if [ -n "$TOKEN" ]; then
        CONFIRM_CANCEL=$(send_telegram_command "/confirm $TOKEN")
        log_info "Potwierdzenie anulowania: $CONFIRM_CANCEL"
    fi
fi

log_success "Workflow zakończony"
exit_test "$TEST_ID" "PASS" "Workflow pobierania zakończony pomyślnie"
