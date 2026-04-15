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
- An Anthropic API key, provided through `ANTHROPIC_API_KEY` or an `appsettings*.json` file

## Configuration

Aexon resolves Anthropic settings in this order:

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
  "Aexon": {
    "Anthropic": {
      "apiKey": "YOUR_ANTHROPIC_API_KEY",
      "baseUrl": "https://api.anthropic.com"
    }
  }
}
```

## Getting Started

Run from source:

```bash
dotnet restore Aexon.slnx
dotnet build Aexon.slnx --configuration Release

# Interactive mode
dotnet run --project src/Aexon.Cli

# Non-interactive prompt
dotnet run --project src/Aexon.Cli -- "explain this codebase"
```

Run from an installed tool:

```bash
# Interactive mode
aexon

# Prompt mode
aexon "explain this codebase"

# Override working directory and model
aexon --cwd /path/to/project --model opus "summarize this repo"

# Resume the latest session
aexon --continue
```

## Hooks And MCP Settings

Unless `--settings` is provided, Aexon merges matching `settings.json` files from these locations:

- `~/.aexon/settings.json`
- `~/.claude/settings.json`
- `<working directory>/.aexon/settings.json`
- `<working directory>/.claude/settings.json`

The current MCP implementation supports stdio servers and registers their tools dynamically at startup.

## Project Memory Files

Aexon loads project instructions from these files when present:

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
- `/mailbox`
- `/team`, `/team create`, `/team show`, `/team dissolve`

## Dependencies

| Package | Purpose |
|---------|---------|
| [Anthropic SDK](https://www.nuget.org/packages/Anthropic) 12.9.0 | Claude API client |
| Microsoft.Extensions.FileSystemGlobbing 10.0.0 | Glob-based file discovery |
| [Spectre.Console](https://spectreconsole.net/) 0.54.0 | Terminal UI rendering |
| Microsoft.Extensions.DependencyInjection 10.0.5 | DI container |

## Architecture

The core loop in `QueryEngine` follows a standard agentic pattern:

1. Build the system prompt from environment, tools, memory, and runtime context
2. Send the conversation to the Claude API
3. Stream the assistant turn, with a buffered fallback available
4. Execute requested tools with local permission checks
5. Append tool results to the conversation
6. Repeat until the model stops calling tools

All I/O is async (`IAsyncEnumerable<QueryEvent>`) so the REPL can stream output progressively.

## License

Released under the MIT License.
