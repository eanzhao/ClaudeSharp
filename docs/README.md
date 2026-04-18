# Aexon 文档总览

> Aexon 现在有两条主线：**本地 coding agent**（继承自 Claude Code）和 **NyxID / Aevatar / Chrono-Storage 三件套 CLI**。这套 `docs/` 主要沉淀前一条线 —— 跟上游 Claude Code 的对照、核心链路的移植。三件套集成这条线直接看仓库根的 [README.md](../README.md)。

这套文档主要回答三件事：

1. `claude-code/` 里那份 TypeScript 快照，整体是怎么跑起来的。
2. 现在这个 C# 项目到底实现到了哪一步，核心链路是怎么接上的。
3. 两边一一对照时，哪些能力已经复刻，哪些地方还做了简化，哪些还没接上。

## 建议阅读顺序

1. [Claude Code 实现解析](./claude-code/implementation.md)
2. [Aexon 实现解析](./claudesharp/implementation.md)
3. [Claude Code 与 Aexon 对照表](./compare/claude-code-vs-claudesharp.md)

## 目录结构

```text
docs/
├── README.md
├── claude-code/
│   └── implementation.md
├── claudesharp/
│   └── implementation.md
├── compare/
│   └── claude-code-vs-claudesharp.md
├── extracted-components.md
└── plans/
    └── implementation-roadmap.md
```

## 这套文档的依据

这几份文档不是空想出来的，主要基于仓库里已经存在的代码路径：

- Claude Code 侧：`claude-code/src/`
- Aexon 侧：`src/Aexon.Cli/`、`src/Aexon.Core/`、`src/Aexon.Tools/`、`src/Aexon.Commands/`
- 测试侧：`tests/Aexon.Core.Tests/`

其中 `claude-code/` 是仓库里保存的一份源码快照，所以这里讲的“Claude Code 怎么实现”，本质上是在解释这份快照里能看到的实现方式，不把它当成官方架构白皮书来写。

## 你会在这里看到什么

- 不只讲“有什么功能”，还会讲“入口在哪里、数据怎么流、状态怎么落盘”。
- 尽量把说明落到具体文件，而不是泛泛地说“有个模块负责这个”。
- 对 Aexon 会明确区分：
  - 已经在线路上跑起来的主路径
  - 从 Claude Code 借鉴过来的设计
  - 当前还没覆盖、或者做了简化的部分

## 补充材料

- [抽出来的可复用组件](./extracted-components.md)
- [实现路线图](./plans/implementation-roadmap.md)

