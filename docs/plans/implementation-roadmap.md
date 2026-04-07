# ClaudeSharp 实现路线图

> Claude Code (.NET 10 / C#) 完整重实现 — 按核心价值排序，最精华部分优先

---

## 当前状态速览

| 模块 | 完成度 | 说明 |
|------|--------|------|
| CLI 骨架 + REPL | 90% | 参数解析、交互循环、流式输出均已实现 |
| QueryEngine (Agentic Loop) | 95% | 核心循环完整，多轮工具调用正常工作 |
| Tool 类型系统 + Registry | 85% | ITool 接口 + 注册表已实现，缺流式进度 |
| 核心工具 (5个) | 85% | Bash/Read/Write/Edit/Grep 已有，缺 Glob/Agent 等 |
| Permission System | 90% | 四种模式定义，Default 模式可用 |
| Context Provider | 85% | Git 状态 + CLAUDE.md + 工具描述均已接入 |
| Commands (5个) | 50% | help/clear/cost/model/exit |
| Token Cost Tracking | 80% | 基础累计可用，缺实时显示 |
| 消息模型 | 100% | ConversationMessage 类型完整 |
| Terminal UI | 20% | Spectre.Console 已引入但未充分利用 |

**结论**: Phase 0 (核心引擎) 已基本完成，应从 Phase 1 开始精细化。

---

## 架构全景

```
┌─────────────────────────────────────────────────────────┐
│                 CLI Entry (Program.cs)                   │ ✅ 已有
├─────────────────────────────────────────────────────────┤
│          Terminal UI (REPL + Rich Output)                │ 🔶 需增强
├─────────────────────────────────────────────────────────┤
│         QueryEngine (核心 Agentic Loop)                  │ ✅ 已有
│      StreamingToolExecutor (流式工具调度)                  │ ⬜ 待实现
├─────────────────────────────────────────────────────────┤
│    Permission System         │    AppState (会话状态)     │ 🔶/⬜
├─────────────────────────────────────────────────────────┤
│  Tools (50+)  │  Commands (170+)  │  Skills / Agents    │ 🔶/⬜/⬜
├─────────────────────────────────────────────────────────┤
│  Services: API │ MCP │ Compact │ Hooks │ Session Storage │ 🔶/⬜/⬜
├─────────────────────────────────────────────────────────┤
│         Config / Settings / Plugins / Remote             │ ⬜
└─────────────────────────────────────────────────────────┘
```

---

## Phase 1: 精细化核心 + 补齐关键缺口

> 目标: 让已有骨架达到生产可用水平，补齐每天使用都会遇到的功能。

### 1.1 StreamingToolExecutor — 流式工具调度 ★★★★★

**为什么重要**: 当前 QueryEngine 在收到完整响应后才批量执行工具。原版在流式接收过程中就开始准备工具执行，极大提升响应速度。

**原始代码**: `src/services/tools/StreamingToolExecutor.ts` (~540 行)

**核心机制**:
```
API 流 ──→ ContentBlockStart(tool_use) ──→ 开始累积 JSON input
         ──→ ContentBlockDelta           ──→ 拼接 partial JSON
         ──→ ContentBlockStop            ──→ JSON 完整，触发执行
         ──→ MessageStop                 ──→ 收集所有结果，返回
```

**关键设计点**:
- 并发 vs 互斥: 只读工具可并发执行，写工具串行
- 进度报告: 工具执行期间持续 yield ProgressEvent
- 超时控制: 每个工具有独立超时
- 取消传播: 用户 Ctrl+C 能取消正在执行的工具

**C# 实现方向**:
```csharp
public class StreamingToolExecutor
{
    public async IAsyncEnumerable<ToolExecutionEvent> ExecuteToolsAsync(
        IReadOnlyList<ToolUseBlock> toolCalls,
        ToolRegistry registry,
        PermissionSystem permissions,
        CancellationToken ct)
    {
        // 分组: concurrent-safe vs exclusive
        var readOnly = toolCalls.Where(t => registry.Get(t.Name).IsReadOnly(t.Input));
        var mutating = toolCalls.Except(readOnly);

        // 并发执行只读工具
        await Parallel.ForEachAsync(readOnly, ct, async (call, token) => { ... });

        // 串行执行写入工具 (每个需权限确认)
        foreach (var call in mutating) { ... }
    }
}
```

**文件**: 新建 `src/ClaudeSharp.Core/Tools/StreamingToolExecutor.cs`

---

### 1.2 GlobTool — 文件模式匹配 ★★★★

**为什么重要**: Claude 日常使用中 Glob 和 Grep 是最高频的两个工具。已有 Grep，缺 Glob。

**原始代码**: `src/tools/GlobTool/GlobTool.ts` (~250 行)

**功能**:
- 输入 glob pattern (如 `**/*.cs`, `src/**/test_*.py`)
- 遍历目录树匹配文件
- 尊重 `.gitignore` 规则
- 按修改时间排序返回
- 支持 `path` 参数限定搜索目录

**C# 实现要点**:
- 使用 `Microsoft.Extensions.FileSystemGlobbing` NuGet
- 或 `Directory.EnumerateFiles` + 自定义 glob 匹配
- gitignore 解析: 可用 `MAB.DotIgnore` 或自己写一个简单的

**文件**: 新建 `src/ClaudeSharp.Tools/GlobTool.cs`

---

### 1.3 Bash 分类器增强 ★★★★

**为什么重要**: 当前 BashTool 用硬编码命令列表判断只读/写入，非常脆弱。原版有一个启发式分类器。

**原始代码**: `src/utils/permissions/bashClassifier.ts` (~350 行)

**分类维度**:
```
命令字符串
  ↓ 解析首个 token (命令名)
  ↓ 查已知命令表 (git, ls, cat, rm, mv, ...)
  ↓ 检查管道/重定向 (> >> | && ||)
  ↓ 检查危险 flag (--force, -rf, --hard)
  ↓ 输出: ReadOnly | Write | Destructive | Unknown
```

**实现建议**:
- 用 `Dictionary<string, CommandCategory>` 做命令查表
- 正则检测重定向和管道
- 特殊处理 `git` 子命令 (`git log` = 只读, `git push` = 写入)

**文件**: 增强 `src/ClaudeSharp.Tools/BashTool.cs` 或新建 `BashClassifier.cs`

---

### 1.4 Session Persistence — 会话持久化 ★★★

**为什么重要**: 原版能恢复中断的会话 (`--resume`)，这对长任务至关重要。

**原始代码**: `src/utils/sessionStorage.ts` (~400 行)

**机制**:
- 存储路径: `~/.claude/sessions/<session_id>/transcript.jsonl`
- 格式: 每行一条 JSON (JSONL)
- 写入策略: 用户消息同步写入 (保证可恢复)，助手消息异步批量写入 (100ms 合并)
- 元数据: session id, working directory, model, timestamp

**C# 实现**:
```csharp
public class SessionStorage
{
    private readonly string _sessionDir;
    private readonly Channel<string> _writeQueue;

    public async Task AppendAsync(ConversationMessage msg, bool sync = false);
    public IAsyncEnumerable<ConversationMessage> LoadAsync(string sessionId);
}
```

**文件**: 新建 `src/ClaudeSharp.Core/Storage/SessionStorage.cs`

---

### 1.5 Terminal UI 增强 ★★★

**为什么重要**: 当前输出是纯文本，缺少视觉反馈。用户体验差距大。

**要增强的点**:
- 工具执行时显示 spinner + 工具名
- 权限确认用 Spectre.Console 的 `Confirm()` 或 styled prompt
- Markdown 渲染 (代码块语法高亮)
- 代价/token 数在底栏显示
- 错误信息用红色

**Spectre.Console 已引入**, 只需接入:
```csharp
AnsiConsole.Status().Start("Running BashTool...", ctx => { ... });
AnsiConsole.MarkupLine("[green]✓[/] File written: path/to/file");
AnsiConsole.MarkupLine("[red]✗[/] Permission denied");
```

**文件**: 新建 `src/ClaudeSharp.Cli/ConsoleRenderer.cs`

---

## Phase 2: Agent 系统 + 消息压缩 + 完整命令

> 目标: 支持复杂任务编排和长上下文会话。这是 Claude Code 区别于普通 chatbot 的关键。

### 2.1 Agent / Subagent 系统 ★★★★★

**为什么重要**: AgentTool 是 Claude Code 最强大的能力之一 — 让 Claude 能启动独立子代理并行处理子任务。

**原始代码**: `src/tools/AgentTool/AgentTool.ts` (~800 行) + `src/tasks/` (~1,200 行)

**架构**:
```
主 QueryEngine
  ├─ AgentTool.Execute("研究 API 设计")
  │    └─ 子 QueryEngine (独立消息历史, 受限工具集)
  │         ├─ GrepTool → 搜索代码
  │         ├─ FileReadTool → 读文件
  │         └─ 返回总结文本给主 Engine
  │
  ├─ AgentTool.Execute("修复测试")
  │    └─ 子 QueryEngine (可在 worktree 中隔离运行)
  │         ├─ BashTool → 运行测试
  │         ├─ FileEditTool → 修复代码
  │         └─ 返回结果
  │
  └─ 主 Engine 汇总两个子代理结果，继续推理
```

**关键设计**:
- 子代理继承父级的 permission context
- 子代理工具集可限制 (如 Explore 类型子代理无写入工具)
- 子代理的 token 用量计入总预算
- 支持并行运行多个子代理 (`run_in_background`)
- Worktree 隔离: `git worktree add` 创建临时工作副本

**C# 实现要点**:
```csharp
public class AgentTool : ITool
{
    private readonly Func<AgentConfig, QueryEngine> _engineFactory;

    public async Task<ToolResult> ExecuteAsync(JsonElement input, ToolUseContext ctx, CancellationToken ct)
    {
        var config = new AgentConfig
        {
            Prompt = input.GetProperty("prompt").GetString()!,
            SubagentType = input.GetOptionalString("subagent_type") ?? "general-purpose",
            RunInBackground = input.GetOptionalBool("run_in_background") ?? false,
            Isolation = input.GetOptionalString("isolation"),  // "worktree"
        };

        var toolSet = ResolveToolSet(config.SubagentType); // 按类型限制工具
        var engine = _engineFactory(config);

        var result = new StringBuilder();
        await foreach (var evt in engine.RunAsync(messages, ct))
        {
            if (evt is TextDeltaEvent td) result.Append(td.Text);
        }
        return ToolResult.Success(result.ToString());
    }
}
```

**文件**: 新建 `src/ClaudeSharp.Tools/AgentTool.cs`

---

### 2.2 消息压缩 (Conversation Compaction) ★★★★

**为什么重要**: 长会话会超出上下文窗口。压缩系统让 Claude Code 能处理小时级别的持续工作。

**原始代码**: `src/services/compact/compact.ts` (~800 行) + `compactSummary.ts` (~600 行)

**触发条件**: 当消息总 token 超过上下文窗口的 ~75%

**两种策略**:

1. **自动压缩 (Auto-compact)**:
   - 调用 Claude 总结前 N 轮对话
   - 插入 SystemMessage 作为压缩边界
   - 丢弃边界前的原始消息
   - 保留最近 K 轮完整对话

2. **工具摘要 (Tool-use Summary)**:
   - 长工具结果截断为摘要
   - 保留工具名 + 输入参数 + 简短输出
   - 大幅减少 token 占用

**C# 实现**:
```csharp
public class CompactionService
{
    public async Task<CompactionResult> CompactAsync(
        List<ConversationMessage> messages,
        int contextWindowTokens,
        CancellationToken ct)
    {
        var totalTokens = EstimateTokens(messages);
        if (totalTokens < contextWindowTokens * 0.75) return CompactionResult.NoChange;

        // 找到压缩点 (保留最近 K 轮)
        var splitIndex = FindCompactionBoundary(messages);

        // 用 Claude 总结前半部分
        var summary = await SummarizeAsync(messages[..splitIndex], ct);

        // 替换
        var compacted = new List<ConversationMessage>
        {
            ConversationMessage.System($"[Conversation compacted]\n{summary}"),
            ..messages[splitIndex..]
        };

        return new CompactionResult(compacted, originalTokens: totalTokens);
    }
}
```

**文件**: 新建 `src/ClaudeSharp.Core/Services/CompactionService.cs`

---

### 2.3 完整 Command 系统 ★★★

**高价值命令** (按使用频率排序):

| 命令 | 功能 | 实现复杂度 |
|------|------|-----------|
| `/compact` | 手动触发压缩 | 低 (调用 CompactionService) |
| `/resume` | 恢复之前的会话 | 中 (需 SessionStorage) |
| `/status` | 当前会话状态 (tokens, model, cwd) | 低 |
| `/config` | 查看/修改设置 | 中 |
| `/permissions` | 管理权限规则 | 中 |
| `/diff` | 显示当前会话的所有文件变更 | 低 (git diff) |
| `/undo` | 撤销最近的文件操作 | 中 |

---

### 2.4 Hook 系统 ★★

**原始代码**: `src/utils/hooks/` (~1,000 行)

**Hook 类型**:
- `SessionStart` — 会话开始时 (如自动注入上下文)
- `PreToolUse` — 工具执行前 (可修改/拦截)
- `PostToolUse` — 工具执行后 (可审计)
- `UserPromptSubmit` — 用户输入提交时

**配置** (在 `settings.json` 中):
```json
{
  "hooks": {
    "PreToolUse": [
      { "command": "bash -c 'echo checking...'", "timeout": 5000 }
    ]
  }
}
```

**文件**: 新建 `src/ClaudeSharp.Core/Hooks/HookRegistry.cs`

---

## Phase 3: MCP + Skill + 高级功能

> 目标: 可扩展性和生态集成。

### 3.1 MCP (Model Context Protocol) 集成 ★★★★

**为什么重要**: MCP 让 Claude Code 能接入任意外部工具服务器 — 数据库、API、IDE 等。是生态扩展的基石。

**原始代码**: `src/services/mcp/` (~9,500 行, 25+ 文件)

**核心流程**:
```
settings.json 配置 MCP server
  ↓
启动 MCP 连接 (stdio 子进程 / SSE HTTP)
  ↓
初始化握手 (capabilities 交换)
  ↓
tools/list → 发现远程工具 → 注册到 ToolRegistry
  ↓
Claude 调用 MCP 工具 → 转发到 MCP server → 返回结果
```

**分步实现**:
1. **MCP 传输层**: stdio (Process 子进程) + SSE (HttpClient)
2. **协议实现**: JSON-RPC 2.0 over 传输层
3. **工具发现**: `tools/list` → 转换为 ITool
4. **资源发现**: `resources/list` → 可选读取
5. **配置加载**: 从 `~/.claude/settings.json` 读取 MCP server 配置

**推荐**: 使用 [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) .NET SDK

**文件**: 新建 `src/ClaudeSharp.Core/Services/Mcp/` 目录

---

### 3.2 Skill 系统 ★★★

**原始代码**: `src/skills/` (~1,500 行)

**机制**:
- Skill = Markdown 文件 (frontmatter + prompt body)
- 存放在 `~/.claude/skills/` 或项目 `.claude/skills/`
- 用户通过 `/skill-name` 调用
- Skill 内容注入 system prompt
- 支持参数 (`/skill arg1 arg2`)

```markdown
---
name: commit
description: Create well-formatted git commits
tools: [Bash, FileRead]
---

When creating a commit, follow these rules:
1. Run git status first
2. Stage specific files (not git add .)
3. ...
```

**文件**: 新建 `src/ClaudeSharp.Core/Skills/SkillLoader.cs`

---

### 3.3 Plan Mode + Worktree 隔离 ★★

**Plan Mode**:
- 进入后 Claude 只规划不执行
- 用户审批计划后切换到执行模式
- 已审批的操作自动获得权限

**Worktree**:
- `git worktree add /tmp/claude-xyz -b agent/task-name`
- 子代理在隔离副本中工作
- 完成后 merge 或丢弃

---

### 3.4 ToolSearch — 延迟工具加载 ★★

**原始代码**: `src/tools/ToolSearchTool/` (~200 行)

**问题**: 50+ 工具的 schema 描述占用大量 system prompt token
**解决**: 只注册高频工具 + ToolSearch。Claude 需要稀有工具时调用 ToolSearch，动态获取 schema。

---

### 3.5 高级配置 ★

- `~/.claude/settings.json` — 用户全局配置
- `.claude/settings.json` — 项目级配置
- Remote Managed Settings — 组织策略下发
- Feature Flags — 功能开关

---

## 原始代码学习指南

按学习价值排序。建议按此顺序阅读原始 TypeScript 代码:

### Tier 1: 必读 (理解 Claude Code 灵魂)

| 文件 | 路径 (相对 `claude-code/src/`) | 学什么 |
|------|------|--------|
| **query loop** | `query.ts` | agentic 循环的完整实现: API 调用 → 工具检测 → 执行 → 递归 |
| **QueryEngine** | `QueryEngine.ts` | 引擎的公共 API, submitMessage 生成器 |
| **API client** | `services/api/claude.ts` | SSE 流处理, 重试逻辑, token 计数 |
| **Tool executor** | `services/tools/StreamingToolExecutor.ts` | 流式环境下如何调度和执行工具 |
| **Tool types** | `Tool.ts` | 工具接口定义, ToolUseContext, 权限协商 |

### Tier 2: 重要 (理解工程质量)

| 文件 | 路径 | 学什么 |
|------|------|--------|
| **permissions** | `utils/permissions/permissions.ts` | 三层权限模型 (deny → allow → ask) |
| **bash classifier** | `utils/permissions/bashClassifier.ts` | 命令安全分类启发式 |
| **messages** | `utils/messages.ts` | 消息类型体系, API 格式转换 |
| **context builder** | `context.ts` | system prompt 的完整组装过程 |
| **tools registry** | `tools.ts` | 工具注册、过滤、feature gate |

### Tier 3: 进阶 (理解可扩展性)

| 文件 | 路径 | 学什么 |
|------|------|--------|
| **AgentTool** | `tools/AgentTool/AgentTool.ts` | 子代理架构, worktree 隔离 |
| **compaction** | `services/compact/compact.ts` | 长上下文压缩策略 |
| **MCP client** | `services/mcp/client.ts` | 外部工具协议集成 |
| **session storage** | `utils/sessionStorage.ts` | JSONL 持久化, 异步写入 |
| **AppState** | `state/AppState.tsx` | 全局状态管理模式 |
| **hook system** | `utils/hooks/` | 生命周期钩子扩展点 |

---

## 实现顺序总结

```
Phase 1: 精细化核心 ─────────────────────── 立即开始
  ├─ 1.1 StreamingToolExecutor (流式调度)    ★★★★★  最大提升
  ├─ 1.2 GlobTool (文件搜索)                ★★★★   日常必需
  ├─ 1.3 Bash 分类器增强                    ★★★★   安全性
  ├─ 1.4 Session Persistence               ★★★    会话恢复
  └─ 1.5 Terminal UI 增强                   ★★★    用户体验

Phase 2: 能力扩展 ───────────────────────── 核心稳定后
  ├─ 2.1 Agent / Subagent 系统              ★★★★★  核心差异化能力
  ├─ 2.2 消息压缩                           ★★★★   长会话必备
  ├─ 2.3 完整 Command 系统                  ★★★    实用性
  └─ 2.4 Hook 系统                          ★★     可扩展性

Phase 3: 生态集成 ───────────────────────── 功能完善后
  ├─ 3.1 MCP 集成                           ★★★★   生态基石
  ├─ 3.2 Skill 系统                         ★★★    用户自定义
  ├─ 3.3 Plan Mode / Worktree              ★★     安全工作流
  ├─ 3.4 ToolSearch (延迟加载)              ★★     性能优化
  └─ 3.5 高级配置                           ★      企业特性
```

---

## 技术决策

### 已确定
- **框架**: .NET 10, C# latest
- **API SDK**: Anthropic NuGet v12.9.0
- **Terminal UI**: Spectre.Console 0.54.0
- **DI**: Microsoft.Extensions.DependencyInjection 10.0.5
- **JSON**: System.Text.Json (内置)

### 待决策
- [ ] Glob 实现: `Microsoft.Extensions.FileSystemGlobbing` vs 自写
- [ ] MCP SDK: `ModelContextProtocol` NuGet vs 自写 JSON-RPC
- [ ] 进程管理: `System.Diagnostics.Process` vs `CliWrap`
- [ ] 测试框架: xUnit vs NUnit
- [ ] CI: GitHub Actions vs 其他
