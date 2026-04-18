# Session Extensions + Model Capabilities Implementation Plan

> **For implementers:** This plan is designed to be executed by the Codex CLI under supervision. Each task is independently shippable, with tests and acceptance criteria. Commit after each task.

**Goal:** Borrow four patterns from pi-sharp into Aexon: (1) extension lifecycle with a minimal session builder, (3) rich model capabilities + pricing metadata, (4) explicit tool-batch execution mode override. Task #2 (full session API boundary) is deferred to a separate issue; this plan only does the minimum session builder needed to support extensions.

**Architecture:** All three tasks live inside `Aexon.Core` to respect the AGENTS.md guardrail "边界语义收口到 Core". Task 3 extends existing types (`ModelCapability`, `ClaudeModelDescriptor`). Task 4 adds a mode enum to `QueryEngineConfig` and threads it into `StreamingToolExecutor`. Task 1 introduces a new `Extensions/` namespace with `IAexonExtension` + `IAexonSessionBuilder` and wires it into `Aexon.Cli/Program.cs` startup before `QueryEngine` is constructed.

**Tech Stack:** .NET 10, C# 13, xUnit 2.9 for tests. No new NuGet dependencies.

**AGENTS.md constraints to respect:**
- 单一主链路 — don't create parallel implementations; extend existing types
- 强类型优先 — no `Dictionary<string, object>`, no magic strings
- 删除优于兼容空壳 — remove any code superseded by changes; no compat shims
- 变更必须可验证 — every task ships with xUnit tests under `tests/Aexon.Core.Tests/`

---

## Task Ordering

Tasks are ordered by increasing scope/risk. Commit after each. Each task must land green (`dotnet test`) before the next starts.

1. **Task A — Model capabilities + pricing** (smallest, zero consumer changes)
2. **Task B — Tool execution mode override** (small, one consumer change)
3. **Task C — Session extensions + minimal session builder** (largest, touches CLI startup)

---

## Task A: Enrich `ModelCapability` and add `ModelPricing`

**Motivation:** Current `ModelCapability` at `src/Aexon.Core/Providers/ModelCapabilityRouting.cs` only has `WebFetch | WebSearch`. Pricing is implicit. This blocks richer `/cost`, `/stats`, `/llm` UX and makes cost estimation fragile. Pi-sharp's pattern: capability flags + tiered pricing attached to the model descriptor.

**Files:**
- Modify: `src/Aexon.Core/Providers/ModelCapabilityRouting.cs` (extend enum)
- Create: `src/Aexon.Core/Providers/ModelPricing.cs`
- Modify: `src/Aexon.Core/Query/ClaudeModelCatalog.cs` (attach to descriptor)
- Modify any call sites of `ClaudeModelDescriptor` constructor — search with `Grep` for `new ClaudeModelDescriptor(`
- Test: `tests/Aexon.Core.Tests/Providers/ProviderCapabilityRouterTests.cs` (extend existing)
- Test: `tests/Aexon.Core.Tests/Providers/ModelPricingTests.cs` (new)

### Spec

**1. Extend `ModelCapability`** to include the full set from pi-sharp. Keep `WebFetch` / `WebSearch` — do not remove existing bits.

```csharp
[Flags]
public enum ModelCapability
{
    None = 0,
    WebFetch = 1 << 0,
    WebSearch = 1 << 1,
    ImageInput = 1 << 2,
    Streaming = 1 << 3,
    ToolCalling = 1 << 4,
    Reasoning = 1 << 5,        // i.e. thinking/extended-thinking
    PromptCaching = 1 << 6,
}
```

**2. Create `ModelPricing`** in `src/Aexon.Core/Providers/ModelPricing.cs`. Rates are USD per 1,000,000 tokens (match pi-sharp convention).

```csharp
namespace Aexon.Core.Providers;

/// <summary>
/// Per-million-token pricing for a model. Values are USD.
/// </summary>
public sealed record ModelPricing(
    decimal InputPer1M,
    decimal OutputPer1M,
    decimal CacheReadPer1M,
    decimal CacheWritePer1M)
{
    public static ModelPricing Unknown { get; } = new(0m, 0m, 0m, 0m);

    public decimal EstimateCostUsd(
        long inputTokens,
        long outputTokens,
        long cacheReadTokens = 0,
        long cacheWriteTokens = 0)
    {
        return
            inputTokens * InputPer1M / 1_000_000m +
            outputTokens * OutputPer1M / 1_000_000m +
            cacheReadTokens * CacheReadPer1M / 1_000_000m +
            cacheWriteTokens * CacheWritePer1M / 1_000_000m;
    }
}
```

**3. Extend `ClaudeModelDescriptor`** to hold capabilities + pricing. Backward-compatible default-valued params so existing call sites compile, but update all current descriptors with real values.

```csharp
public sealed record ClaudeModelDescriptor(
    string StableId,
    string SourceCanonicalId,
    ClaudeModelFamily Family,
    ClaudeModelProviderIds ProviderIds,
    IReadOnlyList<string> Aliases,
    ModelCapability Capabilities = ModelCapability.None,
    ModelPricing? Pricing = null)
{
    public ModelPricing EffectivePricing => Pricing ?? ModelPricing.Unknown;
    // keep existing GetMatchers()
}
```

**4. Fill in capabilities + pricing** for every model in `ClaudeModelCatalog.Models`. All Claude 3.5+/4.x models get:

```csharp
Capabilities: ModelCapability.Streaming | ModelCapability.ToolCalling
            | ModelCapability.PromptCaching | ModelCapability.ImageInput
```

Claude 3.7 / 4.x additionally get `| ModelCapability.Reasoning` (thinking is supported from 3.7 onward).

Pricing (USD / 1M tokens, source: Anthropic public pricing as of 2026-04):
- **Haiku 3.5**: input 0.80, output 4.00, cache-read 0.08, cache-write 1.00
- **Haiku 4.5**: input 1.00, output 5.00, cache-read 0.10, cache-write 1.25
- **Sonnet 3.5 / 3.7 / 4 / 4.5 / 4.6**: input 3.00, output 15.00, cache-read 0.30, cache-write 3.75
- **Opus 4 / 4.1 / 4.5 / 4.6**: input 15.00, output 75.00, cache-read 1.50, cache-write 18.75

(If the implementer finds more up-to-date numbers in existing Aexon code via `Grep` for "per_1m" / "InputCost" / similar, prefer those.)

**5. Wire through router.** `DefaultProviderCapabilityRouter.Resolve` currently hardcodes `ModelCapability.WebFetch` and computes `WebSearch` via `SupportsWebSearch`. Change so **capabilities come from the descriptor first**, then `WebFetch` / `WebSearch` get layered on per provider-kind rules (since those are provider-gated, not model-gated). Net effect: descriptor tells you what the model CAN do; router tells you what the current provider route EXPOSES.

```csharp
var capabilities = descriptor.Capabilities;
// Provider-gated additions (current logic):
capabilities |= ModelCapability.WebFetch;
if (SupportsWebSearch(descriptor, provider))
    capabilities |= ModelCapability.WebSearch;
```

**6. No consumers of pricing yet.** This task just exposes the data. Wiring to `/cost` / `/stats` is out of scope (would be a separate small task).

### Tests (Task A)

Add to `tests/Aexon.Core.Tests/Providers/`:

**ModelPricingTests.cs** — new file:
```csharp
using Aexon.Core.Providers;

namespace Aexon.Core.Tests.Providers;

public class ModelPricingTests
{
    [Fact]
    public void EstimateCost_ZeroTokens_ReturnsZero()
    {
        var pricing = new ModelPricing(3m, 15m, 0.3m, 3.75m);
        Assert.Equal(0m, pricing.EstimateCostUsd(0, 0, 0, 0));
    }

    [Fact]
    public void EstimateCost_SumsAllFourBuckets()
    {
        var pricing = new ModelPricing(3m, 15m, 0.3m, 3.75m);
        // 1M input, 1M output, 1M cache-read, 1M cache-write
        var cost = pricing.EstimateCostUsd(1_000_000, 1_000_000, 1_000_000, 1_000_000);
        Assert.Equal(3m + 15m + 0.3m + 3.75m, cost);
    }

    [Fact]
    public void Unknown_ZeroRates()
    {
        var cost = ModelPricing.Unknown.EstimateCostUsd(1_000_000, 1_000_000);
        Assert.Equal(0m, cost);
    }
}
```

**ProviderCapabilityRouterTests.cs** — extend existing. Add tests:
- `Resolve_Sonnet46_IncludesStreamingAndPromptCaching()` — asserts descriptor capabilities bleed through the router.
- `Resolve_Sonnet46_PricingMatchesCatalog()` — retrieves descriptor and confirms Pricing is non-null with expected InputPer1M.
- `Resolve_Haiku35_DoesNotIncludeReasoning()` — negative check on Reasoning flag.

### Acceptance Criteria (Task A)

- `dotnet build Aexon.slnx -c Debug` clean (no new warnings beyond the pre-existing CS8425)
- `dotnet test tests/Aexon.Core.Tests/Aexon.Core.Tests.csproj` passes, including the 3 new tests and extended router tests
- Every entry in `ClaudeModelCatalog.Models` has non-null `Pricing` and meaningful `Capabilities`
- No consumer needed `Pricing` before this change; nothing else regresses

### Commit (Task A)

```
feat(providers): enrich ModelCapability flags and add ModelPricing

- Extend ModelCapability with ImageInput/Streaming/ToolCalling/Reasoning/PromptCaching
- Add ModelPricing record with per-1M rates and EstimateCostUsd helper
- Attach Capabilities + Pricing to every ClaudeModelDescriptor
- Router now sources model capabilities from descriptor, layers WebFetch/WebSearch
  on top per provider-kind rules
```

---

## Task B: Explicit `ToolBatchExecutionMode` override

**Motivation:** `StreamingToolExecutor` auto-partitions into concurrent vs sequential based on each tool's `IsConcurrencySafe(input)`. No global override exists for debugging (force Sequential) or aggressive parallel fan-out (force Parallel). Pi-sharp exposes both as user-level toggles.

**Files:**
- Create: `src/Aexon.Core/Tools/ToolBatchExecutionMode.cs`
- Modify: `src/Aexon.Core/Query/QueryEngineConfig.cs` (add field)
- Modify: `src/Aexon.Core/Tools/StreamingToolExecutor.cs` (consume mode)
- Modify: `src/Aexon.Core/Query/QueryEngine.cs` (pass mode when constructing executor, if it does)
- Test: `tests/Aexon.Core.Tests/Tools/` (new test file — name `StreamingToolExecutorModeTests.cs`)

### Spec

**1. Enum**:

```csharp
namespace Aexon.Core.Tools;

/// <summary>
/// Controls how a tool-use batch is executed.
/// </summary>
public enum ToolBatchExecutionMode
{
    /// <summary>
    /// Per-tool <see cref="ITool.IsConcurrencySafe"/> decides. Default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Force every tool in a batch to run sequentially regardless of its
    /// concurrency-safety declaration. Useful for debugging and reproducible ordering.
    /// </summary>
    Sequential = 1,

    /// <summary>
    /// Force every tool in a batch to run in parallel regardless of its
    /// concurrency-safety declaration. Caller accepts responsibility for any ordering
    /// or filesystem-interleaving hazards.
    /// </summary>
    Parallel = 2,
}
```

**2. `QueryEngineConfig`** — add a field:

```csharp
public ToolBatchExecutionMode ToolExecutionMode { get; set; } = ToolBatchExecutionMode.Auto;
```

**3. `StreamingToolExecutor`** — plumb the mode through:

Constructor gains an optional parameter:
```csharp
public StreamingToolExecutor(
    ToolRegistry registry,
    IPermissionChecker permissions,
    IHookRuntime? hooks = null,
    ToolBatchExecutionMode mode = ToolBatchExecutionMode.Auto)
```

In `RunBatchAsync`, the partitioning block at lines ~32-71 becomes:

```csharp
var useConcurrent = _mode switch
{
    ToolBatchExecutionMode.Sequential => false,
    ToolBatchExecutionMode.Parallel => true,
    _ => tool.IsConcurrencySafe(invocation.Input),
};
if (useConcurrent) concurrentCandidates.Add(candidate); else sequentialCandidates.Add(candidate);
```

**4. `QueryEngine` constructor** — where it builds the default `StreamingToolExecutor` (line ~91), pass `config.ToolExecutionMode`:

```csharp
_toolRuntime = toolRuntime ?? new StreamingToolExecutor(tools, permissions, effectiveHooks, config.ToolExecutionMode);
```

**5. No CLI flag in this task.** Surfacing it via `/mode parallel` or similar belongs to a follow-up; the config field is enough for programmatic control and tests.

### Tests (Task B)

Create `tests/Aexon.Core.Tests/Tools/StreamingToolExecutorModeTests.cs`:

Design a tiny fake tool that records the timestamp of entry and exit, with `IsConcurrencySafe(input) = false`. Submit a batch of 3, under each mode:

- **Auto** → the three tools should execute sequentially (entry[1] > exit[0], etc.) because the fake reports unsafe.
- **Sequential** → same result as Auto for this fake (trivial but documents intent).
- **Parallel** → at least two invocations overlap in time (entry[1] < exit[0] OR entry[2] < exit[0]).

Use a `SemaphoreSlim` inside the fake tool to create detectable parallelism (tool waits on a gate that only releases once all three have arrived).

Additionally test a safe fake tool (`IsConcurrencySafe=true`):
- **Auto** → parallel (current behavior preserved)
- **Sequential** → forced serial (new behavior)

### Acceptance Criteria (Task B)

- `dotnet build Aexon.slnx -c Debug` clean
- `dotnet test` passes, including the new `StreamingToolExecutorModeTests`
- Existing `Tools/` tests still pass unchanged
- `QueryEngineConfig.ToolExecutionMode` defaults to `Auto`; existing call sites compile without changes

### Commit (Task B)

```
feat(tools): add ToolBatchExecutionMode override for StreamingToolExecutor

Expose Auto/Sequential/Parallel modes via QueryEngineConfig. Auto is the default
and preserves per-tool IsConcurrencySafe behavior; Sequential/Parallel force the
whole batch one way or the other for debugging and explicit fan-out.
```

---

## Task C: `IAexonExtension` + minimal `IAexonSessionBuilder`

**Motivation:** Aexon's hooks (`HookObserver`) are event-driven. There is no lifecycle hook that runs once at session startup to register tools / commands / observers. Pi-sharp's `ICodingAgentExtension.ConfigureSessionAsync(builder)` solves this cleanly. This task lands the minimal surface needed: a builder that extensions receive during CLI startup, before `QueryEngine` is constructed.

**Scope boundary:** This is NOT a full session API façade. The builder is a startup-only configuration object. After startup completes, the builder is discarded. Full session façade is tracked in [eanzhao/Aexon#79](https://github.com/eanzhao/Aexon/issues/79) for future work.

**Files:**
- Create: `src/Aexon.Core/Extensions/IAexonExtension.cs`
- Create: `src/Aexon.Core/Extensions/IAexonSessionBuilder.cs`
- Create: `src/Aexon.Core/Extensions/ExtensionRegistry.cs`
- Modify: `src/Aexon.Cli/Program.cs` (invoke extensions after core wiring, before `QueryEngine` construction)
- Test: `tests/Aexon.Core.Tests/Extensions/ExtensionRegistryTests.cs` (new)

### Spec

**1. `IAexonExtension`**:

```csharp
namespace Aexon.Core.Extensions;

/// <summary>
/// A pre-session extension that can register tools, commands, hook observers, and
/// system-prompt fragments during startup. Lifecycle is one-shot:
/// <see cref="ConfigureAsync"/> runs exactly once before the conversation loop starts.
/// </summary>
public interface IAexonExtension
{
    /// <summary>
    /// Stable identifier shown in diagnostics (e.g. "/doctor" output).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures the session being built. The builder is disposed after all
    /// extensions have run; do not retain references to it.
    /// </summary>
    Task ConfigureAsync(IAexonSessionBuilder builder, CancellationToken cancellationToken);
}
```

**2. `IAexonSessionBuilder`** — minimal surface, no runtime methods:

```csharp
using Aexon.Core.Commands;
using Aexon.Core.Hooks;
using Aexon.Core.Tools;

namespace Aexon.Core.Extensions;

/// <summary>
/// Configuration surface handed to <see cref="IAexonExtension"/> during startup.
/// All registrations are additive; attempts to register a tool or command whose
/// name is already taken throw <see cref="InvalidOperationException"/>.
/// </summary>
public interface IAexonSessionBuilder
{
    /// <summary>Resolved working directory for this session.</summary>
    string WorkingDirectory { get; }

    /// <summary>Model ID the session will start with (e.g. "claude-sonnet-4-6").</summary>
    string Model { get; }

    /// <summary>Registers a tool with the session's <see cref="ToolRegistry"/>.</summary>
    void RegisterTool(ITool tool);

    /// <summary>Registers a slash command with the session's <see cref="CommandRegistry"/>.</summary>
    void RegisterCommand(ICommand command);

    /// <summary>Registers a hook observer with the session's <see cref="HookRuntime"/>.</summary>
    void RegisterHookObserver(HookObserver observer);

    /// <summary>
    /// Appends a fragment to the system prompt. Fragments are concatenated
    /// in registration order after the core prompt.
    /// </summary>
    void AppendSystemPromptFragment(string fragment);
}
```

**3. `ExtensionRegistry`** — a simple list-and-run helper:

```csharp
namespace Aexon.Core.Extensions;

public sealed class ExtensionRegistry
{
    private readonly List<IAexonExtension> _extensions = [];

    public void Add(IAexonExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _extensions.Add(extension);
    }

    public IReadOnlyList<IAexonExtension> Registered => _extensions;

    public async Task RunAsync(IAexonSessionBuilder builder, CancellationToken ct)
    {
        foreach (var ext in _extensions)
        {
            ct.ThrowIfCancellationRequested();
            await ext.ConfigureAsync(builder, ct);
        }
    }
}
```

**4. Concrete builder implementation** — lives in `Aexon.Cli` (not `Aexon.Core`), because it wraps CLI-owned registries. Name: `SessionBuilder` in `src/Aexon.Cli/SessionBuilder.cs`.

```csharp
internal sealed class SessionBuilder : IAexonSessionBuilder
{
    private readonly ToolRegistry _tools;
    private readonly CommandRegistry _commands;
    private readonly HookRuntime _hooks;
    private readonly List<string> _promptFragments = [];

    public SessionBuilder(
        string workingDirectory,
        string model,
        ToolRegistry tools,
        CommandRegistry commands,
        HookRuntime hooks)
    {
        WorkingDirectory = workingDirectory;
        Model = model;
        _tools = tools;
        _commands = commands;
        _hooks = hooks;
    }

    public string WorkingDirectory { get; }
    public string Model { get; }
    public IReadOnlyList<string> PromptFragments => _promptFragments;

    public void RegisterTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (_tools.Get(tool.Name) != null)
            throw new InvalidOperationException($"Tool '{tool.Name}' is already registered.");
        _tools.Register(tool);
    }

    public void RegisterCommand(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_commands.Get(command.Name) != null)
            throw new InvalidOperationException($"Command '{command.Name}' is already registered.");
        _commands.Register(command);
    }

    public void RegisterHookObserver(HookObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _hooks.Register(observer);
    }

    public void AppendSystemPromptFragment(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return;
        _promptFragments.Add(fragment);
    }
}
```

(Implementer: if `ToolRegistry.Get` / `CommandRegistry.Get` don't exist in the current shape, use whatever lookup they expose. Do not change `ToolRegistry` / `CommandRegistry` public surface unless strictly necessary.)

**5. Program.cs wiring** — find the spot in `src/Aexon.Cli/Program.cs` where all core registries are populated but `QueryEngine` has not yet been constructed. Insert:

```csharp
var extensionRegistry = new ExtensionRegistry();
// Built-in extensions (none yet; users add via custom hosting)
var sessionBuilder = new SessionBuilder(workingDirectory, model, toolRegistry, commandRegistry, hooks);
await extensionRegistry.RunAsync(sessionBuilder, CancellationToken.None);
// Propagate prompt fragments into ContextProvider so the system prompt picks them up
foreach (var fragment in sessionBuilder.PromptFragments)
    contextProvider.AppendSystemPromptFragment(fragment);
```

For the prompt fragment wiring: if `ContextProvider` doesn't currently expose `AppendSystemPromptFragment`, add the smallest method that accumulates fragments and appends them after the base system prompt is built. Search for where the system prompt is assembled (likely `ContextProvider.GetSystemPromptAsync()` or similar); add the fragments at the end.

**6. No dynamic DLL discovery in this task.** Extensions are registered in-process only. Dynamic plugin loading (Assembly.LoadFrom, settings-driven discovery) is a follow-up.

**7. Discoverability.** Add a one-line entry to the output of `/doctor` showing extension count (something like `Extensions: 0 registered`) — check what `/doctor` currently prints, and extend minimally. If `/doctor` is complex, skip this and open a follow-up — don't let it balloon the task.

### Tests (Task C)

Create `tests/Aexon.Core.Tests/Extensions/ExtensionRegistryTests.cs`:

```csharp
using Aexon.Core.Extensions;

namespace Aexon.Core.Tests.Extensions;

public class ExtensionRegistryTests
{
    private sealed class RecordingExtension : IAexonExtension
    {
        public string Name => "recording";
        public bool Ran { get; private set; }
        public Task ConfigureAsync(IAexonSessionBuilder builder, CancellationToken ct)
        {
            Ran = true;
            builder.AppendSystemPromptFragment("hello from extension");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBuilder : IAexonSessionBuilder
    {
        public string WorkingDirectory => "/tmp";
        public string Model => "claude-sonnet-4-6";
        public List<string> Fragments { get; } = [];
        public void RegisterTool(Aexon.Core.Tools.ITool _) { }
        public void RegisterCommand(Aexon.Core.Commands.ICommand _) { }
        public void RegisterHookObserver(Aexon.Core.Hooks.HookObserver _) { }
        public void AppendSystemPromptFragment(string fragment) => Fragments.Add(fragment);
    }

    [Fact]
    public async Task RunAsync_CallsEveryRegisteredExtensionInOrder()
    {
        var ext1 = new RecordingExtension();
        var ext2 = new RecordingExtension();
        var registry = new ExtensionRegistry();
        registry.Add(ext1);
        registry.Add(ext2);
        var builder = new FakeBuilder();

        await registry.RunAsync(builder, CancellationToken.None);

        Assert.True(ext1.Ran);
        Assert.True(ext2.Ran);
        Assert.Equal(2, builder.Fragments.Count);
    }

    [Fact]
    public async Task RunAsync_ReturnsImmediatelyOnEmptyRegistry()
    {
        var registry = new ExtensionRegistry();
        var builder = new FakeBuilder();
        await registry.RunAsync(builder, CancellationToken.None);
        Assert.Empty(builder.Fragments);
    }
}
```

Also add an integration test (smaller, not exhaustive) that uses the real `SessionBuilder` and a real `ToolRegistry` to verify a fake tool registered via `RegisterTool` ends up in the registry.

### Acceptance Criteria (Task C)

- `dotnet build Aexon.slnx -c Debug` clean
- `dotnet test` passes, including new `Extensions/` tests
- With zero registered extensions, CLI startup is byte-identical to before (no behavior change)
- With a hand-registered test extension that calls `AppendSystemPromptFragment`, the fragment appears at the end of the system prompt sent to the model (verifiable by asserting on `ContextProvider` output)
- No parallel implementation: `HookRuntime` / `ToolRegistry` / `CommandRegistry` remain the single source of truth; the builder is a thin pass-through

### Commit (Task C)

```
feat(core): introduce IAexonExtension + minimal IAexonSessionBuilder

Extensions run once at startup before QueryEngine is built and can register
tools, commands, hook observers, and system-prompt fragments through a
builder surface. No dynamic DLL discovery yet (in-process registration only).

Scope deliberately minimal; full session API façade tracked separately.
```

---

## Self-Review Checklist

- [ ] Task A: extends `ModelCapability` without removing bits; descriptor-first router logic; pricing record independent and testable.
- [ ] Task B: preserves default Auto behavior; Sequential/Parallel are additive; fake-tool concurrency test actually proves parallelism.
- [ ] Task C: builder lives in `Aexon.Cli`, interface in `Aexon.Core`; extensions run before `QueryEngine` is built; fragments land in system prompt; zero-extension CLI is a no-op.
- [ ] Every task has xUnit tests in the corresponding folder under `tests/Aexon.Core.Tests/`.
- [ ] No new `Dictionary<string, object>`, no magic strings.
- [ ] No parallel implementations — extend existing registries / descriptors / config types.
- [ ] Each task is independently commitable with green build + green tests.

---

## Deferred Work (not in this plan)

- Full session API façade (`AexonSession` type with runtime methods like `SubmitAsync`, `SubscribeEvents`, `GetState`) — tracked in [eanzhao/Aexon#79](https://github.com/eanzhao/Aexon/issues/79).
- Dynamic plugin loading from `~/.aexon/extensions/*.dll`.
- Surfacing `ToolBatchExecutionMode` via a CLI flag or slash command.
- Wiring `ModelPricing` into `/cost` and `/stats` commands.
- Extension-aware `/doctor` output (beyond the minimal line added in Task C).
