# Documentation

This directory contains the current documentation for the Media Server Bot (TorrentBot2).

## Structure

| File                              | Description |
|-----------------------------------|-------------|
| [README.md](README.md)            | This page – documentation overview |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Complete current architecture, principles, components and data flows |
| [ENGINE.md](ENGINE.md)            | Detailed design of the central Engine |
| [FUNCTIONALITY.md](FUNCTIONALITY.md) | What the system does from the user's point of view |
| [PLUGINS_AND_CAPABILITIES.md](PLUGINS_AND_CAPABILITIES.md) | Plugin system and capability model |
| [QUERY.md](QUERY.md)              | Safe query DSL and repository access used by the LLM |
| [LLM.md](LLM.md)                  | LLM planning pipeline, known problems, configuration and fixes |

## How to Read

1. Start with [ARCHITECTURE.md](ARCHITECTURE.md) for the overall picture.
2. Read [ENGINE.md](ENGINE.md) if you are working on the core.
3. Use [PLUGINS_AND_CAPABILITIES.md](PLUGINS_AND_CAPABILITIES.md) when building or extending plugins.
4. Refer to [QUERY.md](QUERY.md) for data access and LLM reasoning.
5. Read [LLM.md](LLM.md) for natural-language planning, Ollama setup and troubleshooting.
6. Check [FUNCTIONALITY.md](FUNCTIONALITY.md) to understand user-facing capabilities.

## Principles

- We only document the **current state**.
- Architecture is the single source of truth.
- All documentation is focused on the active .NET implementation.

---

**Current phase:** Architecture & Engine core design complete.
