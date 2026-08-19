# Functionality

## Interfaces

Homelynx exposes explicit Telegram slash commands and an explicit CLI capability interface. Plain chat text is not converted into commands.

## Torrent and download workflow

Typical flow:

1. `/search <query>` searches through Jackett and stores the displayed result set for the current session.
2. `/select <index>` or a Telegram selection button selects an item from that stored result set.
3. Protected download/start actions use the normal confirmation mechanism where required.
4. `/downloads`, `/torrents`, `/pause`, `/resume` and `/cancel` expose current transfer control.
5. Background job monitoring tracks work that continues after the command response.

## System

System capabilities include help/capability listing, health, ping, status, metrics and event diagnostics according to the registered plugin surface.

## Media

Media capabilities provide media-library listing and TTS through the configured filesystem/media and TTS integrations.

## Query

`query.execute` provides structured read-only access to registered runtime snapshot sources through DuckDB.

## Security

ACL rules limit capability access by user. Capabilities declare risk and scope metadata. Operations configured as confirmation-required must receive an explicit confirmation before execution.

## Session state

State is retained only where a deterministic workflow requires it, chiefly search-result selection and pending confirmations. Telegram callback buttons can resolve these pending actions; ordinary text cannot.
