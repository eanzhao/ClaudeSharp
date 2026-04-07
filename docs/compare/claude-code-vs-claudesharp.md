# Claude Code 与 ClaudeSharp 对照表

这份表不追求“功能菜单式罗列”，只回答一个问题：

ClaudeSharp 现在到底把 Claude Code 的哪些核心设计搬过来了，哪些地方是简化版，哪些还没接进去。

## 一张总表

| 能力 | Claude Code | ClaudeSharp | 当前结论 |
| --- | --- | --- | --- |
| 主循环 | `src/QueryEngine.ts` + `src/query.ts` | `src/ClaudeSharp.Core/Query/QueryEngine.cs` | 已复刻核心 agent loop，但当前不是 token 级流式 |
| 工具协议 | `src/Tool.ts` | `src/ClaudeSharp.Core/Tools/ITool.cs` | 已复刻核心接口设计 |
| 工具装配 | `src/tools.ts` | `src/ClaudeSharp.Core/Tools/ToolRegistry.cs` | 已有基础装配，范围更小 |
| 工具执行编排 | `services/tools/toolOrchestration.ts` + `services/tools/StreamingToolExecutor.ts` | `src/ClaudeSharp.Core/Tools/StreamingToolExecutor.cs` | 已有并发/串行批处理，但还不是 Claude Code 完整流式调度 |
| 基础工具 | Bash / Read / Write / Edit / Glob / Grep 等大量工具 | Bash / Read / Write / Edit / Glob / Grep | 基础 coding 工具到位，外围工具大量未实现 |
| 系统提示拼装 | `src/context.ts` + `utils/systemPrompt.ts` + `utils/queryContext.ts` | `src/ClaudeSharp.Core/Context/ContextProvider.cs` | 已复刻核心思路 |
| `CLAUDE.md` 规则加载 | `utils/claudemd.ts` | `MemoryInstructionScanner.cs` + `FrontmatterParser.cs` | 已复刻常用路径扫描和 frontmatter 处理 |
| 权限模式 | 多模式 + 规则源 + shell 专项权限 | `PermissionSystem.cs` + `PermissionRules.cs` | 已有基础版，策略深度还没到原版 |
| Bash 分类 | `utils/permissions/bashClassifier.ts` + `tools/BashTool/bashPermissions.ts` | `BashCommandClassifier.cs` + `BashTool.cs` | 已有可用的启发式分类 |
| transcript 持久化 | `utils/sessionStorage.ts` | `TranscriptStore.cs` | 已实现 JSONL transcript + manifest |
| resume / 恢复 | `utils/sessionRestore.ts` | `ConversationRecovery.cs` + `SessionRestorePipeline.cs` | 已实现恢复主链路 |
| compact | `services/compact/compact.ts` | `ConversationCompactor.cs` | 已实现本地总结式 compact |
| microcompact | `services/compact/microCompact.ts` | `MicroCompactor.cs` | 已实现本地版 |
| session memory | `services/compact/sessionMemoryCompact.ts` | `SessionMemoryCompactor.cs` | 已实现本地版 |
| 上下文压力管线 | `services/compact/autoCompact.ts` 及相关模块 | `ContextPressurePipeline.cs` + `AutoCompactPolicy.cs` | 已接入请求前自动处理 |
| 模型目录 | `utils/model/*` | `ClaudeModelCatalog.cs` | 已有统一 catalog |
| 斜杠命令 | 大量命令和 UI 配套 | `BuiltinCommands.cs` | 已有基础命令集 |
| 子代理 / 团队代理 | Agent、team、workflow 很完整 | 暂无正式接入主链路 | 明显缺口 |
| MCP | 原版已深度集成 | 仓库里已有 `Mcp/` 方向代码，但 CLI 主路径未正式接线 | 还在路上 |
| Hook / 插件 / bridge / remote | 很完整 | 仅有局部结构雏形或未接入 | 还没进入主战场 |

## 模块级映射

### 1. QueryEngine

- Claude Code：
  - `claude-code/src/QueryEngine.ts`
  - `claude-code/src/query.ts`
- ClaudeSharp：
  - `src/ClaudeSharp.Core/Query/QueryEngine.cs`
  - `src/ClaudeSharp.Core/Query/QueryEvents.cs`
  - `src/ClaudeSharp.Core/Query/QueryEngineConfig.cs`

差异：

- Claude Code 把“会话状态”和“单轮 query loop”拆成两个大模块。
- ClaudeSharp 目前把主循环更多集中在 `QueryEngine.cs` 里，结构更紧凑。
- ClaudeSharp 已经有事件流接口，但 API 调用仍然是响应级，不是原版那种完整流式主循环。

### 2. Tool 协议和执行

- Claude Code：
  - `claude-code/src/Tool.ts`
  - `claude-code/src/tools.ts`
  - `claude-code/src/services/tools/toolExecution.ts`
  - `claude-code/src/services/tools/toolOrchestration.ts`
  - `claude-code/src/services/tools/StreamingToolExecutor.ts`
- ClaudeSharp：
  - `src/ClaudeSharp.Core/Tools/ITool.cs`
  - `src/ClaudeSharp.Core/Tools/ToolRegistry.cs`
  - `src/ClaudeSharp.Core/Tools/ToolRuntime.cs`
  - `src/ClaudeSharp.Core/Tools/StreamingToolExecutor.cs`
  - `src/ClaudeSharp.Tools/*.cs`

差异：

- ClaudeSharp 已经把“工具 = schema + prompt + 权限 + 执行 + 并发属性”这套抽象完整搬过来了。
- Claude Code 的 hook、progress、fallback、sibling abort、UI 联动要复杂得多。
- ClaudeSharp 当前更像一个干净的可测试 runtime 内核。

### 3. 上下文与系统提示

- Claude Code：
  - `claude-code/src/context.ts`
  - `claude-code/src/utils/systemPrompt.ts`
  - `claude-code/src/utils/claudemd.ts`
  - `claude-code/src/utils/frontmatterParser.ts`
- ClaudeSharp：
  - `src/ClaudeSharp.Core/Context/ContextProvider.cs`
  - `src/ClaudeSharp.Core/Context/MemoryInstructionScanner.cs`
  - `src/ClaudeSharp.Core/Markdown/FrontmatterParser.cs`

差异：

- ClaudeSharp 已经保住了最核心的那部分：identity、环境、git、工具 prompt、memory 文件。
- Claude Code 额外还有附件、技能、agent prompt、coordinator prompt、更多 feature flag 逻辑。

### 4. 权限系统

- Claude Code：
  - `claude-code/src/utils/permissions/permissions.ts`
  - `claude-code/src/tools/BashTool/bashPermissions.ts`
  - `claude-code/src/utils/permissions/bashClassifier.ts`
- ClaudeSharp：
  - `src/ClaudeSharp.Core/Permissions/PermissionSystem.cs`
  - `src/ClaudeSharp.Core/Permissions/PermissionRules.cs`
  - `src/ClaudeSharp.Tools/Shell/BashCommandClassifier.cs`

差异：

- 两边都有规则、模式、shell 分类这几个关键概念。
- Claude Code 的权限来源、自动分类器、sandbox、hook 联动更重。
- ClaudeSharp 当前更适合先当“清晰、可测试、够用”的研究实现。

### 5. transcript 与恢复

- Claude Code：
  - `claude-code/src/utils/sessionStorage.ts`
  - `claude-code/src/utils/sessionRestore.ts`
- ClaudeSharp：
  - `src/ClaudeSharp.Core/Storage/TranscriptStore.cs`
  - `src/ClaudeSharp.Core/Storage/ConversationRecovery.cs`
  - `src/ClaudeSharp.Core/Storage/SessionRestorePipeline.cs`

差异：

- ClaudeSharp 已经有 transcript、manifest、leaf chain、metadata、checkpoint、microcompact replay。
- Claude Code 的 transcript 系统还要承载更多历史兼容、subagent transcript、snip/compact 边界重建。

### 6. 长上下文治理

- Claude Code：
  - `claude-code/src/services/compact/*`
- ClaudeSharp：
  - `src/ClaudeSharp.Core/Compaction/*`

ClaudeSharp 已经把 Claude Code 这套治理思路拆成几块清晰的小组件：

- `PromptTokenEstimator`
- `AutoCompactPolicy`
- `MicroCompactor`
- `SessionMemoryCompactor`
- `ConversationCompactor`
- `ContextPressurePipeline`

这一块其实是 ClaudeSharp 当前最“像一个可复用内核库”的部分。

## ClaudeSharp 现在最像 Claude Code 的地方

如果只抓核心设计，ClaudeSharp 已经比较像 Claude Code 的地方有这些：

- 把对话建模成内容块消息链，而不是简单字符串历史
- 把工具建模成统一协议，而不是散落的函数
- 主循环会反复执行“模型输出 -> 工具调用 -> 工具结果回灌 -> 再推理”
- 项目规则不是一份死 prompt，而是按目录扫描出来
- transcript 不是聊天记录，而是可恢复执行链
- 长上下文不是“超了就截断”，而是多层压缩策略

## 还明显不像的地方

真正拉开差距的，是这些 Claude Code 的重平台能力：

- 完整的流式工具执行
- 多代理协作
- MCP 深度接入
- Hook、插件、技能生态
- IDE/desktop/remote bridge
- 更复杂的权限与 sandbox 联动
- 更成熟的终端 UI 和可视化状态

这些部分 ClaudeSharp 目前还没有全面展开。

## 应该怎么评价当前进度

比较准确的说法不是“已经重写完 Claude Code”，而是：

- ClaudeSharp 已经把 Claude Code 最核心、最值得研究的执行内核搬过来了。
- 当前完成度最高的是：QueryEngine、工具系统、权限、上下文、持久化、压缩。
- 当前还没做满的是：平台化、扩展系统、流式 UI、多代理和外部集成。

如果后面继续迭代，ClaudeSharp 最自然的路线不是“盲目加功能”，而是顺着现在这套抽象继续补：

1. 真正的流式 API 主循环
2. 更完整的工具执行钩子
3. MCP 动态工具接线
4. 子代理与工作流
5. 更强的会话和 UI 能力

