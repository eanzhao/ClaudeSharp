# Claude Code 源码里提出来的可复用 C# 组件

这次没有去碰 `tsx` UI、远程桥接、MCP 编排这种强耦合模块，而是优先抽了几块以后单独拿出来也能用的内核。

## 1. 权限规则匹配器

- C# 实现：`src/ClaudeSharp.Core/Permissions/PermissionRules.cs`
- 接入点：`src/ClaudeSharp.Core/Permissions/PermissionSystem.cs`
- Claude Code 来源：
  - `claude-code/src/utils/permissions/PermissionRule.ts`
  - `claude-code/src/utils/permissions/shellRuleMatching.ts`

现在支持三种规则形态：

- `Bash(git status)`：精确匹配
- `Bash(git:*)`：前缀匹配
- `Read(/tmp/*.md)`：通配符匹配

这个组件的价值在于，它不绑死在 Bash 上。只要工具输入里有 `command`、`file_path` 或 `path`，就能复用同一套规则语义。

## 2. Markdown Frontmatter 解析器

- C# 实现：`src/ClaudeSharp.Core/Markdown/FrontmatterParser.cs`
- Claude Code 来源：`claude-code/src/utils/frontmatterParser.ts`

保留了几件很实用的能力：

- 从 Markdown 头部提取 YAML frontmatter
- 第一次 YAML 解析失败时，对容易炸掉的值自动补引号再重试
- 解析 `paths` 这种逗号分隔 + 花括号展开写法
- 解析正整数和布尔 frontmatter 字段

这块以后无论是做技能加载、规则文件、命令清单还是文档元数据，都能直接拿来用。

## 3. 指令文件扫描器

- C# 实现：`src/ClaudeSharp.Core/Context/MemoryInstructionScanner.cs`
- 接入点：`src/ClaudeSharp.Core/Context/ContextProvider.cs`
- Claude Code 来源：`claude-code/src/utils/claudemd.ts`

现在 `ContextProvider` 不再只会傻读当前目录的 `CLAUDE.md`，而是会从根目录一路扫到当前目录，按顺序收集：

- `CLAUDE.md`
- `CLAUDE.local.md`
- `.claude/CLAUDE.md`
- `.claude/rules/*.md`

另外会自动剥掉 rule 文件的 frontmatter，并把 `paths` 范围注到 prompt 头里。

## 4. 模型目录

- C# 实现：`src/ClaudeSharp.Core/Query/ClaudeModelCatalog.cs`
- 接入点：`src/ClaudeSharp.Core/Query/ClaudeModels.cs`
- Claude Code 来源：
  - `claude-code/src/utils/model/configs.ts`
  - `claude-code/src/utils/model/aliases.ts`
  - `claude-code/src/utils/model/model.ts`

这块把以前零散的 alias 映射升级成了一个真正的 catalog，统一管理：

- 稳定模型 ID
- Claude Code 源码里的 canonical ID
- Bedrock / Vertex / Foundry 的 provider-specific ID
- `sonnet` / `opus` / `haiku` 这类人类输入别名

后面如果要做 `/model` 高级选项、provider 适配或者模型 allowlist，这个表就能直接复用。

## 5. Bash 退出码语义解释器

- C# 实现：`src/ClaudeSharp.Tools/Shell/CommandSemantics.cs`
- 接入点：`src/ClaudeSharp.Tools/BashTool.cs`
- Claude Code 来源：`claude-code/src/tools/BashTool/commandSemantics.ts`

以前 `BashTool` 只要退出码不是 0，就只能机械地把 `Exit code: N` 拼出来。现在会按命令语义解释：

- `rg` / `grep` 的 `1` 视为“没搜到”，不是错误
- `diff` 的 `1` 视为“有差异”，不是执行失败
- `test` / `[` 的 `1` 视为“条件不成立”

这让工具结果更像 Claude Code 本身，也更适合抽成独立 shell 组件。
