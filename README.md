# ClaudeSharp

A .NET 10 reimplementation of [Claude Code](https://docs.anthropic.com/en/docs/claude-code) — Anthropic's agentic coding CLI — written in C#.

## Overview

ClaudeSharp provides an interactive terminal REPL where Claude can execute tools (shell commands, file operations, code search) to assist with software engineering tasks. It faithfully reproduces the core agentic loop architecture of the original TypeScript implementation using idiomatic .NET patterns.

This project is maintained for **educational and security research** purposes, studying agentic developer tooling architecture and software supply-chain practices. See [`claude-code/README.md`](claude-code/README.md) for full research context.

## Features

- **Interactive REPL** — multi-turn conversation with streaming responses
- **Agentic tool loop** — Claude autonomously calls tools, validates inputs locally, and iterates with permission checks
- **Built-in tools** — Bash, file read/write/edit, glob/grep, web fetch/search
- **Subagents and background runs** — a read-only `Agent` tool plus `/agents` commands for inspection, waiting, tailing, pruning, and stop requests
- **Hooks and MCP** — load lifecycle hooks and stdio MCP servers from `settings.json`
- **Permission system** — read-only operations auto-approved, write operations prompt for confirmation
- **Slash commands** — session, compaction, and agent-management commands in addition to the REPL basics
- **Session persistence** — transcripts are stored as JSONL and can be resumed with `--continue` / `--resume`, or forked with `--fork-session`
- **Context compaction** — compact, microcompact, and session-memory flows are built into the main loop
- **Token cost tracking** — input/output/cache token counts with cost estimation
- **Context-aware prompts** — git status, working directory, project memory (CLAUDE.md)

## Project Structure

```
src/
├── ClaudeSharp.Cli/          # Entry point, REPL shell, CLI option parsing
├── ClaudeSharp.Core/         # QueryEngine, agents, hooks, MCP, permissions, context, storage
├── ClaudeSharp.Tools/        # Built-in tools, web tools, and subagent-facing tools
└── ClaudeSharp.Commands/     # Built-in slash commands (/help, /agents, /compact, /session, ...)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100+)
- An Anthropic API key, provided through `ANTHROPIC_API_KEY` or an `appsettings*.json` file

## Configuration

ClaudeSharp resolves Anthropic settings in this order:

1. `ANTHROPIC_API_KEY`
2. `<working directory>/appsettings.secrets.json`
3. `<working directory>/appsettings.json`
4. `<app base directory>/appsettings.secrets.json`
5. `<app base directory>/appsettings.json`

`appsettings.secrets.json` is the recommended place for local secrets and is ignored by git in this repository.

Both of the following JSON shapes are supported:

```json
{
  "Anthropic": {
    "apiKey": "YOUR_ANTHROPIC_API_KEY",
    "baseUrl": "https://api.anthropic.com"
  }
}
```

```json
{
  "ClaudeSharp": {
    "Anthropic": {
      "apiKey": "YOUR_ANTHROPIC_API_KEY",
      "baseUrl": "https://api.anthropic.com"
    }
  }
}
```

## Getting Started

```bash
# Build
dotnet build

# Run (interactive mode)
dotnet run --project src/ClaudeSharp.Cli

# Or place your key in ./appsettings.secrets.json first
cat > appsettings.secrets.json <<'JSON'
{
  "Anthropic": {
    "apiKey": "YOUR_ANTHROPIC_API_KEY"
  }
}
JSON

# Run with an initial prompt (non-interactive)
dotnet run --project src/ClaudeSharp.Cli -- "explain this codebase"

# Specify working directory and model
dotnet run --project src/ClaudeSharp.Cli -- --cwd /path/to/project --model opus "your prompt"

# Resume the latest session
dotnet run --project src/ClaudeSharp.Cli -- --continue
```

## Hooks And MCP Settings

Hooks and MCP server definitions are merged from the matching `settings.json` files in these locations unless `--settings` is provided:

- `~/.claudesharp/settings.json`
- `~/.claude/settings.json`
- `<working directory>/.claudesharp/settings.json`
- `<working directory>/.claude/settings.json`

The current MCP implementation supports stdio servers and registers their tools dynamically at startup.

## Test And Coverage

```bash
# Run the full test suite
dotnet test tests/ClaudeSharp.Core.Tests/ClaudeSharp.Core.Tests.csproj --no-restore

# Enforce the total project line-coverage gate
dotnet test tests/ClaudeSharp.Core.Tests/ClaudeSharp.Core.Tests.csproj --configuration Release --no-restore \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=json \
  /p:CoverletOutput=TestResults/coverage-threshold/ \
  /p:Threshold=80 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total
```

The GitHub Actions workflow in `.github/workflows/ci.yml` runs the same coverage gate on every push and pull request.

## CLI Options

| Option | Description |
|--------|-------------|
| `--cwd <path>` | Set the working directory for the session |
| `--model <name>` | Model name or alias (`sonnet`, `opus`, `haiku`) |
| `--resume <session>` | Resume a specific session by id, directory, manifest, or transcript path |
| `--continue` | Resume the most recently updated session |
| `--fork-session` | Fork the resumed transcript into a brand new session |
| `--settings <path>` | Load hooks and MCP servers from a specific `settings.json` |
| `--mcp-config` | Alias for `--settings` |
| `--help` | Show help |
| `<prompt>` | Initial prompt; omit for interactive mode |

## Slash Commands

- `/help`, `/clear`, `/exit`, `/cost`, `/model`
- `/session`, `/mode`, `/title`, `/tag`
- `/compact`, `/microcompact`, `/pcompact`, `/session-memory`
- `/agents`, `/agents summary`, `/agents list`, `/agents wait`, `/agents tail`, `/agents prune`, `/agents stop`

## Dependencies

| Package | Purpose |
|---------|---------|
| [Anthropic SDK](https://www.nuget.org/packages/Anthropic) 12.9.0 | Claude API client |
| Microsoft.Extensions.FileSystemGlobbing 10.0.0 | Glob-based file discovery |
| [Spectre.Console](https://spectreconsole.net/) 0.54.0 | Terminal UI rendering |
| Microsoft.Extensions.DependencyInjection 10.0.5 | DI container |

## Architecture

The core loop in `QueryEngine` follows the standard agentic pattern:

1. Build system prompt (environment, tools, memory)
2. Send conversation to Claude API
3. Stream the assistant turn when the streaming path is enabled, with a buffered fallback available
4. Execute requested tools (with permission checks)
5. Append tool results to conversation
6. Repeat from step 2 until Claude stops calling tools

All I/O is async (`IAsyncEnumerable<QueryEvent>`) so the REPL can stream output progressively.

## License

This project is for educational and research purposes. See [`claude-code/README.md`](claude-code/README.md) for research context and ethical considerations.
