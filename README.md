# ClaudeSharp

A .NET 10 reimplementation of [Claude Code](https://docs.anthropic.com/en/docs/claude-code) — Anthropic's agentic coding CLI — written in C#.

## Overview

ClaudeSharp provides an interactive terminal REPL where Claude can execute tools (shell commands, file operations, code search) to assist with software engineering tasks. It faithfully reproduces the core agentic loop architecture of the original TypeScript implementation using idiomatic .NET patterns.

This project is maintained for **educational and security research** purposes, studying agentic developer tooling architecture and software supply-chain practices. See [`claude-code/README.md`](claude-code/README.md) for full research context.

## Features

- **Interactive REPL** — multi-turn conversation with streaming responses
- **Agentic tool loop** — Claude autonomously calls tools, observes results, and iterates
- **Built-in tools** — Bash, FileRead, FileWrite, FileEdit, Glob, Grep
- **Permission system** — read-only operations auto-approved, write operations prompt for confirmation
- **Slash commands** — `/help`, `/clear`, `/cost`, `/model`, `/exit`
- **Session persistence** — transcripts are stored as JSONL and can be resumed with `--continue` / `--resume`
- **Token cost tracking** — input/output/cache token counts with cost estimation
- **Context-aware prompts** — git status, working directory, project memory (CLAUDE.md)

## Project Structure

```
src/
├── ClaudeSharp.Cli/          # Entry point & REPL shell
├── ClaudeSharp.Core/         # Abstractions: QueryEngine, ToolRegistry, Permissions, Context, Storage
├── ClaudeSharp.Tools/        # Tool implementations (Bash, FileRead, FileWrite, FileEdit, Glob, Grep)
└── ClaudeSharp.Commands/     # Built-in slash commands (/help, /clear, /cost, /model)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100+)
- `ANTHROPIC_API_KEY` environment variable

## Getting Started

```bash
# Build
dotnet build

# Run (interactive mode)
dotnet run --project src/ClaudeSharp.Cli

# Run with an initial prompt (non-interactive)
dotnet run --project src/ClaudeSharp.Cli -- "explain this codebase"

# Specify working directory and model
dotnet run --project src/ClaudeSharp.Cli -- --cwd /path/to/project --model opus "your prompt"

# Resume the latest session
dotnet run --project src/ClaudeSharp.Cli -- --continue
```

## CLI Options

| Option | Description |
|--------|-------------|
| `--cwd <path>` | Set the working directory for the session |
| `--model <name>` | Model name or alias (`sonnet`, `opus`, `haiku`) |
| `--resume <session>` | Resume a specific session by id, directory, manifest, or transcript path |
| `--continue` | Resume the most recently updated session |
| `--help` | Show help |
| `<prompt>` | Initial prompt; omit for interactive mode |

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
3. Stream response — render text, detect tool calls
4. Execute requested tools (with permission checks)
5. Append tool results to conversation
6. Repeat from step 2 until Claude stops calling tools

All I/O is async (`IAsyncEnumerable<QueryEvent>`) so the REPL can stream output progressively.

## License

This project is for educational and research purposes. See [`claude-code/README.md`](claude-code/README.md) for research context and ethical considerations.
