# Homelynx E2E tests

The E2E suite validates the explicit command surface exposed by CLI and Telegram. Natural-language command interpretation is intentionally not supported.

## Run

```bash
./e2e-tests/run-tests.sh
./e2e-tests/run-tests.sh --live
./e2e-tests/run-tests.sh '*' system
```

Tests are grouped under `e2e-tests/tests/` by domain. Live tests use the configured Telegram, Jackett and qBittorrent services; deterministic CLI/HTTP tests should be preferred where external services are not required.

## Required coverage

The maintained surface includes system commands (`/help`, `/health`, `/status`, `/ping`), torrent search and selection (`/search`, `/select`, `/more`, `/torrents`), download operations (`/download`, `/downloads`, `/cancel`) and the existing workflow/media/job cases.

Plain text without a slash is not a command. Telegram callback buttons remain supported for selections and confirmations.
