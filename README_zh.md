# Aexon

[![CI](https://github.com/eanzhao/Aexon/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/eanzhao/Aexon/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Aexon?logo=nuget&label=NuGet)](https://www.nuget.org/packages/Aexon/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aexon?logo=nuget&label=downloads)](https://www.nuget.org/packages/Aexon/)

> English: [README.md](README.md).

Aexon 是一个 .NET 10 写的命令行工具，兼顾两件事：

1. **本地 coding agent** — 从 [Claude Code](https://docs.anthropic.com/en/docs/claude-code) 移植过来的 C# 实现，保留 agentic tool loop、流式输出、会话恢复、hooks、MCP、subagents 等能力，可以当 Claude Code 直接用。
2. **公司三件套的命令行入口** — 把 [NyxID](https://github.com/ChronoAIProject/NyxID)（Agent Connectivity Gateway，给 agent 统一管凭据和网络出口）、[Aevatar](https://aevatar.ai)（Actor + Event 驱动的多 Agent 协作运行时，默认走 Workflow YAML 编排）、Chrono-Storage（Bun + Hono 写的多桶 S3 抽象层）拼进同一个 `aexon` 二进制，让本地开发、脚本、CI 都能用一致的命令形态调用这几个后端。

两条主线互相借力：coding agent 的能力让你在终端里直接跟模型协作，而三件套集成把「登录 / 调用 Agent / 读写对象存储」变成 `aexon login` / `aexon aevatar` / `aexon storage` 这类短命令。

## 两个方向

### 1. 强化本地 coding agent

继承自上游 Claude Code，并在 .NET 生态里继续演进：

- 交互式 REPL + 流式响应 + 非交互 `--print` 模式
- Bash / Read / Write / Edit / Glob / Grep / WebFetch / WebSearch 等内置工具
- Subagents、后台运行、mailbox、team 编排
- Hooks 和 stdio MCP server 从项目或用户级 `settings.json` 加载
- 会话恢复 / fork、压缩 (`/compact` / `/microcompact` / `/pcompact`)、session memory、token 与成本统计
- CLAUDE.md 记忆层：`system → user → project` 合并加载，支持 `.claudeignore`
- Ubuntu / Windows / macOS 全平台 CI，带 80% 行覆盖率门槛
- 以 `Aexon` 这个 .NET 全局工具包发布到 NuGet

### 2. 打通公司三件套

| 产品 | 角色 | 在 aexon 里的入口 |
|------|------|-------------------|
| **NyxID** | Agent Connectivity Gateway：OIDC + API Key 双认证，凭据注入代理 (Credential Injection Proxy)，MCP Tool Wrapping，通过 Credential Node 打到内网和 localhost | `aexon login` / `aexon logout` / `aexon llm`（REPL 里的 `/login` 等同款） |
| **Aevatar** | 多 Agent 协作运行时，Actor + Event 内核（默认 Orleans transport），用 Workflow YAML 声明 `roles + steps + routes`，Chat 通过 SSE / WebSocket 流式观察协作过程 | `aexon aevatar` 子命令族 + `aexon aevatar web`（就地起 Aevatar 工作流 Studio） |
| **Chrono-Storage** | 多桶 S3 抽象（Bun + Hono + AWS SDK v3，兼容 MinIO），提供 bucket / object / presigned URL / batch-delete / cross-bucket copy 等 HTTP 接口 | `aexon storage ls/cat/get/put/put-text/rm`（目前经 Aevatar 的 explorer 代理打过去） |

这三件套各自独立，但在 aexon 里被 NyxID 的 token 串起来：登录一次，`aevatar`、`storage` 都自动复用 `~/.nyxid/` 里的凭据（aexon 自身的偏好如默认 provider/model 另存在 `~/.aexon/preferences.json`，不污染 nyxid 目录）。aexon 本身的 Anthropic / OpenAI 流量也经 NyxID 网关转发，服务端才把真实 API key 注入上游，本地永远看不到 raw key。

## 安装

推荐用 NuGet 装全局工具：

```bash
dotnet tool install --global Aexon
aexon --help
```

升级：

```bash
dotnet tool update --global Aexon
```

装到项目本地的工具目录：

```bash
dotnet tool install --tool-path ./.tools Aexon
./.tools/aexon --help
```

只想下载 `.nupkg`：

```bash
nuget install Aexon -Source https://api.nuget.org/v3/index.json -OutputDirectory ./packages
```

从源码跑（本地开发用）：

```bash
dotnet restore Aexon.slnx
dotnet build Aexon.slnx --configuration Release
dotnet run --project src/Aexon.Cli
```

仓库里的 [scripts/reinstall.sh](scripts/reinstall.sh) 是从当前源码重新打包并重装成全局工具的快捷脚本，适合本地迭代。

## 前置依赖

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100+)
- 一个 [NyxID](https://github.com/ChronoAIProject/NyxID) 账号 —— LLM 请求和（未来的）大部分凭据都走 NyxID 网关而不是本机 `ANTHROPIC_API_KEY` / `OPENAI_API_KEY`。默认实例是 `https://nyx-api.chrono-ai.fun`，自建实例设 `NYXID_BASE_URL`
- 可选：本地 Ollama，地址从 `OLLAMA_HOST` / `OLLAMA_BASE_URL` 读，默认 `http://127.0.0.1:11434`

## 一次性配置

Aexon 不再读 `ANTHROPIC_API_KEY`、`OPENAI_API_KEY` 或 `appsettings*.json`，所有 Anthropic / OpenAI 流量都走 NyxID 的 `/api/v1/llm/gateway`。三步：

1. **在 NyxID 里挂好 provider 凭据**（浏览器打开 `/keys → Add Service`，或者用 NyxID CLI）：

   ```bash
   nyxid service add llm-anthropic --credential-env ANTHROPIC_API_KEY
   nyxid service add llm-openai    --credential-env OPENAI_API_KEY
   ```

2. **从 aexon 登录 NyxID**（浏览器 OAuth，token 存到 `~/.nyxid/`，布局跟上游 nyxid CLI 完全一致，两边共用）：

   ```bash
   aexon login
   ```

3. **选默认 provider**。首次跑 LLM 相关命令（交互式 REPL 或 `aexon "<prompt>"`）时，aexon 会检查 `~/.aexon/preferences.json`，如果还没设过默认就自动跳出配置流程。Picker 同时合并 NyxID 的**两类**来源：

   - **Gateway providers** —— `GET /api/v1/llm/status` 的 auto-seeded LLM provider（`anthropic` / `openai` / …），走 `/api/v1/llm/<slug>/v1/` 路由。
   - **AI Services** —— `GET /api/v1/keys` 返回 dashboard 里所有用户名下的 AI Service（Chrono LLM、Mimo、你手动挂的 OpenAI 兼容端点……）。每条活跃的 http 服务都会被 `GET /api/v1/proxy/s/<slug>/models` 探测一次（NyxID 代理把这个路径转给 `{endpoint_url}/models`，按 NyxID 约定服务的 `endpoint_url` 本身就含 `/v1`）；凡是能返回 OpenAI 风格 `{ data: [{id}, …] }`（或 `{ models: […] }`）的就被识别为 LLM-capable，并附上 model 列表。不合形状的静默跳过。

   合并后的 picker 把 gateway 入口标成 `G1`/`G2`/…，AI Services 标成 `P1`/`P2`/…，按索引或 slug 都能选。选到 AI Service 时还会把探测到的 model 列表展开让你挑编号。

   也可以提前手动配：

   ```bash
   aexon llm                        # 交互式 picker（跟首次引导走同一套逻辑）
   aexon llm use anthropic gpt-4o   # gateway provider
   aexon llm use chrono-llm         # AI Service slug —— 自动选第一个探测到的 model
   aexon llm use proxy:mimo qwen3   # `proxy:` 前缀可选，slug 冲突时显式带上
   ```

   Gateway 默认和 AI-Service 默认互斥，写入一边会清另一边。`aexon llm show` 打印当前生效的那个；`aexon llm list` 把两个表都展开。

   如果是 `--print` 模式或 stdin 不是 TTY，aexon 不会卡在交互提示上 —— 直接打印指引并非零退出。

之后 `aexon "some prompt"` 就直接跑起来了 —— 当前默认如果是 AI Service，aexon 把 chat 作为 OpenAI 兼容请求打到 `/api/v1/proxy/s/<slug>/chat/completions`（NyxID 代理把它转给 `{endpoint_url}/chat/completions`）。

Ollama 是本地 inference，不走 NyxID，按需 `--provider ollama --model <tag>` 即可。

## 快速上手

### 作为本地 coding agent

```bash
# 交互式 REPL
aexon

# 非交互：跑完一个 prompt 就退出
aexon "解释一下这个 repo"

# 机器可读的单次输出
aexon --print --output-format json "summarize this repo"

# 管道喂入 + 拒绝所有权限请求（CI 安全模式）
cat file.py | aexon --print --approval-mode deny "review this code"

# 指定工作目录和模型
aexon --cwd /path/to/project --model opus "summarize this repo"

# 临时换 provider（NyxID 里必须已经 ready）
aexon --provider openai --model gpt-4o "summarize this repo"

# 临时走 Ollama
aexon --provider ollama --model qwen3:4b "summarize this repo"

# 恢复最近一次会话
aexon --continue
```

### 作为三件套 CLI

NyxID 登录 / 身份 / LLM：

```bash
aexon login                 # 浏览器 OAuth
aexon logout                # 撤销 refresh token 并清本地凭据
aexon llm                   # 交互式选默认 provider
aexon llm list              # 看 NyxID 里当前有哪些 provider / 是否 ready
aexon llm use <p> [model]   # 直接设默认
aexon llm show / clear
```

Aevatar —— 跟后端 chat 和起 workflow Studio：

```bash
aexon aevatar                              # 对当前对话开 REPL（默认主网）
aexon aevatar "帮我起草一段说明"            # 在当前会话里追加一条消息并流式接收回复
aexon aevatar new [title]                  # 新建一个对话
aexon aevatar list                         # 看当前 scope 下的对话
aexon aevatar open <id>                    # 切到某个历史对话
aexon aevatar delete [id]                  # 删对话（默认删当前）
aexon aevatar config show                  # 看/改 base URL 和 scope
aexon aevatar config set-url <url>
aexon aevatar config set-scope <scopeId>
aexon aevatar web [--port N] [--no-browser]   # 就地起 Aevatar 工作流 Studio（内置反代 /api/*）
```

Chrono-Storage —— 通过 Aevatar 的 explorer 代理读写对象：

```bash
aexon storage ls [prefix]              # 列文件（可选前缀过滤）
aexon storage cat <key>                # 把文本文件打到 stdout
aexon storage get <key> [local]        # 下载到本地路径（省略则 stdout）
aexon storage put <key> <local>        # 上传二进制（multipart）
aexon storage put-text <key>           # 从 stdin 读文本写入
aexon storage rm <key>
```

这三组命令共用 `AevatarChatSettingsStore` 里记的 base URL 和 scope —— 改 `/aevatar config set-url` 同时也会影响 `/storage`。

## Hooks 与 MCP 设置

不显式传 `--settings` 时，Aexon 会合并以下位置的 `settings.json`：

- `~/.aexon/settings.json`
- `~/.claude/settings.json`
- `<working directory>/.aexon/settings.json`
- `<working directory>/.claude/settings.json`

MCP 目前支持 stdio server，启动时动态注册工具。内置工具用「按需加载」策略：高频工具默认常驻，低频工具（web search、cron、mailbox、team、tasks、remote triggers、monitoring、managed worktrees）延迟到模型调用 `ToolSearch` + `select:ToolA,ToolB` 时再加载。

## 项目记忆 (CLAUDE.md)

加载顺序 `system → user → project`，后写的覆盖前面的。项目层从仓库根向 cwd 递进地扫，尊重 `.claudeignore`，改文件后下次构建 prompt 时自动重载。

记忆来源：

- `<app base>/CLAUDE.md`
- `<app base>/.claude/CLAUDE.md`
- `<app base>/.claude/rules/*.md`
- `~/.claude/CLAUDE.md`
- `~/.claude/rules/*.md`
- `CLAUDE.md`
- `.claude/CLAUDE.md`
- `.claude/rules/*.md`
- `CLAUDE.local.md`

## 测试与覆盖率

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

GitHub Actions 在 push / PR 上跑同样的检查，三平台 (Ubuntu / Windows / macOS) 并行，覆盖率结果作为 artifact 上传。

## 项目结构

```text
src/
├── Aexon.Cli/          # 入口、REPL、CLI 参数解析、组合根
├── Aexon.Core/         # Query engine、agents、hooks、MCP、permissions、context、storage
│   ├── Auth/           # NyxID 登录、token、credential store
│   ├── Aevatar/        # Aevatar chat client + 设置 + chrono-storage client
│   └── …               # providers、tools runtime、compaction、memory 等
├── Aexon.Tools/        # 内置工具、web 工具、subagent 面向的工具
└── Aexon.Commands/     # 斜杠命令：/aevatar、/storage、/agents、/mailbox、/team、/compact …
```

## CLI 参数

| 参数 | 说明 |
|------|------|
| `--cwd <path>` | 会话工作目录 |
| `--model <name>` | 模型名或别名 (`sonnet` / `opus` / `haiku`)，留空则走默认 |
| `--provider <name>` | `anthropic` / `openai` / `ollama`，留空则走默认 |
| `--resume <session>` | 按 id / 目录 / manifest / transcript 路径恢复会话 |
| `--continue` | 恢复最近一次会话 |
| `--fork-session` | 把被恢复的 transcript fork 成一个新会话 |
| `--settings <path>` | 指定 `settings.json` 加载 hooks + MCP |
| `--mcp-config` | `--settings` 的别名 |
| `--print`, `-p` | 单次非交互 prompt |
| `--output-format <text\|markdown\|json>` | `--print` 模式下的输出格式 |
| `--approval-mode <allow\|deny>` | `--print` 模式下对权限请求的非交互策略 |
| `--max-turns <n>` | 单次运行的最大 assistant/tool 轮次 |
| `--help` | 帮助 |
| `<prompt>` | 初始 prompt，省略则进 REPL |

## 斜杠命令（REPL 中）

**会话与模型**：`/help`、`/clear`、`/exit`、`/cost`、`/model`、`/effort`、`/fast`、`/title`、`/tag`、`/session`、`/mode`

**身份与 LLM**：`/login`、`/logout`、`/llm`（也支持 `list / use / show / clear`）

**压缩与记忆**：`/compact`、`/microcompact`、`/pcompact`、`/session-memory`、`/memory`

**编排**：`/agents`（及 `summary / list / wait / tail / prune / stop`）、`/mailbox`、`/team`（及 `create / show / dissolve`）

**三件套**：`/aevatar`（对话 + `web`）、`/storage`（S3 风格读写）

**工程流**：`/diff`、`/review`、`/commit`、`/branch`、`/pr`、`/init`、`/doctor`、`/status`、`/stats`

## 依赖

| 包 | 用途 |
|----|------|
| [Anthropic SDK](https://www.nuget.org/packages/Anthropic) 12.9.0 | Claude API 客户端 |
| [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI) 10.2.0 | 统一的 `IChatClient` 抽象、中间件管线、结构化输出 |
| [Microsoft.Extensions.AI.OpenAI](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI) 10.2.0-preview.1.26063.2 | OpenAI `IChatClient` 适配 |
| [OpenAI](https://www.nuget.org/packages/OpenAI) 2.8.0 | OpenAI 及兼容端点客户端 |
| [OllamaSharp](https://www.nuget.org/packages/OllamaSharp) 5.4.10 | Ollama `IChatClient` 实现 |
| Microsoft.Extensions.FileSystemGlobbing 10.0.0 | Glob 文件发现 |
| [Spectre.Console](https://spectreconsole.net/) 0.54.0 | 终端 UI |
| Microsoft.Extensions.DependencyInjection 10.0.0 | DI 容器 |
| Microsoft.AspNetCore.App 10.0.0 | `aexon aevatar web` 内置的 Kestrel + 反代 |

## 架构

`QueryEngine` 的主循环是标准 agentic 模式：

1. 从环境、工具、记忆、运行时上下文拼 system prompt
2. 把会话交给 MEAI 的 `IChatClient`
3. 流式接收 assistant 回合（也保留了缓冲式 fallback）
4. 执行请求到的工具，本地走 permission check
5. 把工具结果追加回会话
6. 模型不再调工具就收尾

CLI 侧通过 `AddChatClient()` 注册 chat client，在 MEAI 管线上叠加重试、日志、OpenTelemetry、provider 专属 options 映射、结构化输出 helper。所有 I/O 都是异步的 (`IAsyncEnumerable<QueryEvent>`)，REPL 可以持续滚动输出。

## 三件套产品简介

- **NyxID** — Rust 写的 Agent Connectivity Gateway（`~/Code/NyxID`）。核心职责：(1) OIDC + API Key 认证；(2) Credential Injection Proxy —— 托管第三方 API key（Anthropic / OpenAI / Google / Slack …），让 agent 只发可控 token，真实凭据服务端注入；(3) 把底层服务 wrap 成 MCP 工具；(4) 通过 Credential Node 做 NAT 穿透，让 agent 触达内网 / localhost 服务。Aexon 的 `~/.aexon/nyxid.json` 与 NyxID CLI 的 `~/.nyxid/` 共享 token 目录。
- **Aevatar** — .NET 写的多 Agent 协作运行时（`~/Code/aevatar`）。内核是 Actor + Event（默认 Orleans，可切 Kafka / MassTransit transport），编排层是 Workflow YAML —— 在一份 YAML 里声明 `roles + steps + routes`，支持 `llm_call`、`parallel`、`vote_consensus`、`connector_call` 等步骤类型，不需写代码就能组合顺序 / 分支 / 循环 / 并行 / 投票 / 人工审批。对外通过 `POST /api/chat`（SSE / WebSocket）触发并流式观察协作过程。`aexon aevatar` 打到它的 `/api/scopes/{scope}/chat-history` 端点。
- **Chrono-Storage** — Bun + Hono + AWS SDK v3 写的多桶对象存储抽象（`~/Code/chrono-storage`）。不是自己再造一个 S3，而是在任意 S3 兼容后端（AWS S3 / MinIO / …）前面挂一层统一的 HTTP 接口：bucket CRUD、object CRUD、batch-delete-by-prefix、cross-bucket copy、presigned URL 等。默认端口 3805，`GET /health`、`GET /openapi.json`。`aexon storage` 命令当前经 Aevatar 的 explorer 代理打过去。

## 许可证

MIT。
