# Aexon

[![CI](https://github.com/eanzhao/Aexon/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/eanzhao/Aexon/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Aexon?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Aexon/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aexon?logo=nuget&label=downloads)](https://www.nuget.org/packages/Aexon/)

A .NET 10 reimplementation of [Claude Code](https://docs.anthropic.com/en/docs/claude-code), written in C#.

## Overview

Aexon is an interactive terminal coding assistant with an agentic tool loop, streaming responses, resumable sessions, and built-in automation for common engineering workflows.

The project is maintained for educational, interoperability, and security research work around agentic developer tooling. It is usable today, but you should still review permissions, hooks, and external tool access before running it against real codebases.

## Current Status

- Interactive REPL with streaming assistant responses
- Built-in tool loop for Bash, file read/write/edit, glob/grep, web fetch, and web search
- Subagents, background runs, mailbox management, and team orchestration commands
- Hooks and stdio MCP server loading from project or user settings
- Session resume, session forking, compaction, session memory, and token/cost tracking
- Cross-platform CI on Ubuntu, Windows, and macOS with formatting checks, tests, and an 80% total line-coverage gate
- Published on NuGet as the `Aexon` .NET tool package

## Installation

Recommended: install Aexon as a global .NET tool from NuGet.

```bash
dotnet tool install --global Aexon
aexon --help
```

Update an existing installation:

```bash
dotnet tool update --global Aexon
```

Install into a local tool directory instead of your global PATH:

```bash
dotnet tool install --tool-path ./.tools Aexon
./.tools/aexon --help
```

If you specifically want to download the NuGet package artifact, you can use the NuGet CLI:

```bash
nuget install Aexon -Source https://api.nuget.org/v3/index.json -OutputDirectory ./packages
```

`nuget install` downloads the package, while `dotnet tool install` is the command that gives you an executable `aexon` command on your machine.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100+) for local builds and `dotnet` CLI workflows
- A [NyxID](https://github.com/chrono-ai/NyxID) account — Aexon brokers all Anthropic and OpenAI traffic through the NyxID LLM gateway instead of reading local API keys. The default NyxID instance is `https://nyx-api.chrono-ai.fun`; override with `NYXID_BASE_URL` if you run your own.
- Optional: a local Ollama server reachable at `OLLAMA_HOST` or the default `http://127.0.0.1:11434` for offline inference.

## Configuration

Aexon no longer reads `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, or `appsettings*.json`. All Anthropic and OpenAI requests flow through NyxID's LLM gateway, so provider credentials live in one place (NyxID) and Aexon just holds a NyxID session token.

The one-time setup is three commands:

1. **Connect a provider credential in NyxID.** Open the NyxID web console (`/keys` → "Add Service") and add at least one provider — e.g. Anthropic or OpenAI. Or use the NyxID CLI:

   ```bash
   nyxid service add llm-anthropic --credential-env ANTHROPIC_API_KEY
   nyxid service add llm-openai --credential-env OPENAI_API_KEY
   ```

2. **Sign in from Aexon.** This spins up a browser-based OAuth flow and persists tokens to `~/.aexon/nyxid.json`:

   ```bash
   aexon login
   ```

3. **Pick a default provider.** `aexon llm` opens an interactive picker — it calls `GET /api/v1/llm/status`, shows every provider with its readiness, and asks you to choose:

   ```bash
   aexon llm
   # Gateway: https://nyx-api.chrono-ai.fun/api/v1/llm/gateway/v1
   # Providers on https://nyx-api.chrono-ai.fun:
   #    1. anthropic      [ready]   Anthropic
   #    2. openai         [ready]   OpenAI
   #    3. google-ai      [not_connected]  Google AI
   # Pick provider by number or slug: 1
   # Default model (or press Enter for the Aexon default for anthropic):
   # Default LLM set to anthropic. Restart the session to pick it up.
   ```

   Prefer a one-liner? `aexon llm use anthropic` (or `aexon llm use openai gpt-4o`) skips the picker and writes the default directly.

After that, `aexon "some prompt"` routes through NyxID automatically — no flags required.

Ollama stays fully local and is not brokered by NyxID. Its host is read from:

1. `OLLAMA_HOST`
2. `OLLAMA_BASE_URL`
3. default `http://127.0.0.1:11434`

Invoke it explicitly with `--provider ollama --model <tag>` when you want to bypass NyxID.

## Getting Started

Run from source:

```bash
dotnet restore Aexon.slnx
dotnet build Aexon.slnx --configuration Release

# Interactive mode
dotnet run --project src/Aexon.Cli

# Non-interactive prompt
dotnet run --project src/Aexon.Cli -- "explain this codebase"

# Print mode for scripts
dotnet run --project src/Aexon.Cli -- --print --output-format json "explain this codebase"
```

Run from an installed tool:

```bash
# Interactive mode
aexon

# Prompt mode
aexon "explain this codebase"

# Single-shot print mode with JSON output
aexon --print --output-format json "summarize this repo"

# Review piped content non-interactively
cat file.py | aexon --print --approval-mode deny "review this code"

# Override working directory and model
aexon --cwd /path/to/project --model opus "summarize this repo"

# Switch provider for one run (NyxID must have the provider connected and `ready`)
aexon --provider openai --model gpt-4o "summarize this repo"

# Use Ollama (local, does not go through NyxID)
aexon --provider ollama --model qwen3:4b "summarize this repo"

# Resume the latest session
aexon --continue
```

## NyxID account management

Three top-level subcommands cover everything; each one also works with a leading slash inside the REPL (`/login`, `/logout`, `/llm`):

```bash
aexon login                  # browser OAuth against NyxID; tokens land in ~/.aexon/nyxid.json
aexon logout                 # revoke the refresh token on NyxID and clear local creds
aexon llm                    # interactive picker (default)
aexon llm list               # show every LLM provider NyxID exposes and whether it's `ready`
aexon llm use <p> [model]    # non-interactive: set the default provider (anthropic|openai)
aexon llm show               # print the currently selected default
aexon llm clear              # forget the default provider without signing out
```

Both the interactive `aexon llm` flow and the non-interactive `aexon llm use` form validate the choice against `GET /api/v1/llm/status`, so you'll get a clear error if the provider isn't connected on the NyxID side yet. The default is persisted under `~/.aexon/nyxid.json` and survives future `aexon login` refreshes.

Point Aexon at a non-default NyxID instance with `NYXID_BASE_URL` before running `aexon login` (e.g. a self-hosted server).

### How routing works

- Anthropic requests go to `POST <NYXID>/api/v1/llm/anthropic/v1/messages`
- OpenAI requests go to `POST <NYXID>/api/v1/llm/openai/v1/chat/completions`
- Both requests carry `Authorization: Bearer <NyxID access token>`; NyxID injects your stored provider credential server-side and forwards to the upstream API
- Tokens refresh automatically when within 60 seconds of expiry; a 401 triggers a single force-refresh retry before surfacing "Run /login again"

## Hooks And MCP Settings

Unless `--settings` is provided, Aexon merges matching `settings.json` files from these locations:

- `~/.aexon/settings.json`
- `~/.claude/settings.json`
- `<working directory>/.aexon/settings.json`
- `<working directory>/.claude/settings.json`

The current MCP implementation supports stdio servers and registers their tools dynamically at startup.

Built-in tools now also support deferred discovery. Aexon keeps the high-frequency tool set loaded by default, while lower-frequency tools such as web search, cron, mailbox, team coordination, task management, remote triggers, monitoring, and managed worktrees stay deferred until the model calls `ToolSearch` and loads them with `select:ToolA,ToolB`.

## Project Memory Files

Aexon now loads CLAUDE memory in precedence order `system -> user -> project`, then lets the later entries override the earlier ones in the final prompt. Project scanning walks from the current repository root down to the working directory, supports `.claudeignore`, and reloads automatically on the next prompt build after files change.

Memory sources:

- `<app base>/CLAUDE.md`
- `<app base>/.claude/CLAUDE.md`
- `<app base>/.claude/rules/*.md`
- `~/.claude/CLAUDE.md`
- `~/.claude/rules/*.md`
- `CLAUDE.md`
- `.claude/CLAUDE.md`
- `.claude/rules/*.md`
- `CLAUDE.local.md`

## Test And Coverage

Local verification:

```bash
dotnet restore Aexon.slnx
dotnet build Aexon.slnx --configuration Release --no-restore
dotnet format Aexon.slnx --verify-no-changes --no-restore --severity error
dotnet test Aexon.slnx \
  --configuration Release \
  --no-restore \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=json \
  /p:CoverletOutput=TestResults/coverage-ci/ \
  /p:Threshold=80 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total
```

GitHub Actions runs the same checks on push and pull request, across Ubuntu, Windows, and macOS. Coverage results are uploaded as workflow artifacts for each OS job.

## Project Structure

```text
src/
├── Aexon.Cli/          # Entry point, REPL shell, CLI option parsing
├── Aexon.Core/         # Query engine, agents, hooks, MCP, permissions, context, storage
├── Aexon.Tools/        # Built-in tools, web tools, and subagent-facing tools
└── Aexon.Commands/     # Slash commands such as /agents, /mailbox, /team, /compact
```

## CLI Options

| Option | Description |
|--------|-------------|
| `--cwd <path>` | Set the working directory for the session |
| `--model <name>` | Model name or alias (`sonnet`, `opus`, `haiku`) — falls back to the stored default |
| `--provider <name>` | `anthropic`, `openai`, or `ollama` — falls back to the stored default |
| `--resume <session>` | Resume a specific session by id, directory, manifest, or transcript path |
| `--continue` | Resume the most recently updated session |
| `--fork-session` | Fork the resumed transcript into a brand new session |
| `--settings <path>` | Load hooks and MCP servers from a specific `settings.json` |
| `--mcp-config` | Alias for `--settings` |
| `--print`, `-p` | Run a single non-interactive prompt and exit |
| `--output-format <text\|markdown\|json>` | Format for `--print` output |
| `--approval-mode <allow\|deny>` | Non-interactive permission policy for `--print` |
| `--max-turns <n>` | Maximum assistant/tool turns for the run |
| `--help` | Show help |
| `<prompt>` | Initial prompt; omit for interactive mode |

## Slash Commands

- `/help`, `/clear`, `/exit`, `/cost`, `/model`
- `/login`, `/logout`, `/llm` (interactive picker; also accepts `list`, `use`, `show`, `clear`)
- `/session`, `/mode`, `/effort`, `/fast`, `/title`, `/tag`
- `/compact`, `/microcompact`, `/pcompact`, `/session-memory`
- `/agents`, `/agents summary`, `/agents list`, `/agents wait`, `/agents tail`, `/agents prune`, `/agents stop`
- `/mailbox`
- `/team`, `/team create`, `/team show`, `/team dissolve`

## Dependencies

| Package | Purpose |
|---------|---------|
| [Anthropic SDK](https://www.nuget.org/packages/Anthropic) 12.9.0 | Claude API client |
| [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI) 10.2.0 | Unified `IChatClient` abstraction, middleware pipeline, structured output helpers |
| [Microsoft.Extensions.AI.OpenAI](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI) 10.2.0-preview.1.26063.2 | OpenAI `IChatClient` adapter |
| [OpenAI](https://www.nuget.org/packages/OpenAI) 2.8.0 | OpenAI and compatible endpoint client |
| [OllamaSharp](https://www.nuget.org/packages/OllamaSharp) 5.4.10 | Ollama `IChatClient` implementation |
| Microsoft.Extensions.FileSystemGlobbing 10.0.0 | Glob-based file discovery |
| [Spectre.Console](https://spectreconsole.net/) 0.54.0 | Terminal UI rendering |
| Microsoft.Extensions.DependencyInjection 10.0.0 | DI container |

## Architecture

The core loop in `QueryEngine` follows a standard agentic pattern:

1. Build the system prompt from environment, tools, memory, and runtime context
2. Send the conversation through MEAI `IChatClient`
3. Stream the assistant turn, with a buffered fallback available
4. Execute requested tools with local permission checks
5. Append tool results to the conversation
6. Repeat until the model stops calling tools

The CLI now registers chat clients through `AddChatClient()` and layers retries, logging, OpenTelemetry, provider-specific option mapping, and structured-output helpers on the MEAI pipeline.

All I/O is async (`IAsyncEnumerable<QueryEvent>`) so the REPL can stream output progressively.

## License

Released under the MIT License.
