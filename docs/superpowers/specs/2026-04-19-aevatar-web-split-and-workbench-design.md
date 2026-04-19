# Aevatar web command split + Service Workbench prototype

**Date:** 2026-04-19
**Status:** Approved (awaiting user spec review)
**Author:** Claude (working with eanzhao)

## 1. Summary

`aexon aevatar web` today serves an upstream-built React bundle that bundles a Workflow Studio, scripts editor, GAgent type explorer, config explorer, console (chat), and settings — all in one ~1.1 MB minified `app.js`. The semantics of `aevatar web` are muddy because the bundle does five different things and only one of them (the chat console) is really what most users want from this CLI.

This spec splits the web surface into two narrowly-purposed subcommands and lays the groundwork for a richer service-management UI:

- **`aexon aevatar chat`** — a trimmed version of the upstream bundle exposing only the chat console + settings pages. Connects to the real `nyxid-chat` backend immediately.
- **`aexon aevatar web`** — the new "Service Workbench" UI per the design at `https://api.anthropic.com/v1/design/h/RjV9Vz8l6uDDWyVXyce2uA`. Build → Bind → Invoke → Observe four-step service detail page. **In this spec, prototype only — no real backend wiring.** Phase 3 (covered by a future spec) will TSX-ify and wire it.

Old `wwwroot/aevatar/*` is removed. New build artifacts land in `wwwroot/aevatar-chat/` and `wwwroot/aevatar-workbench/`.

## 2. Motivation

Three drivers:

- **Semantic clarity.** A user typing `aexon aevatar web` shouldn't get a workflow studio they have no use for in a CLI tool. Splitting per-purpose subcommands matches how the rest of `aexon aevatar` works (`new`, `list`, `open`, `delete`, etc.).
- **Bundle bloat.** The upstream Workflow Studio bundle pulls in `@xyflow/react`, `@monaco-editor/react`, `monaco-editor`, `@tanstack/react-virtual` plus thousands of lines of editor code. None of that is reachable from a chat-only experience. Trimming should bring the chat bundle from ~1.1 MB to roughly 300-400 KB.
- **Workbench is the future of `aevatar web`.** The design captures the user's vision of how service operators should build/bind/invoke/observe their services. We want it landed structurally now, even as a static prototype, so future iteration has a place to live.

## 3. Goals & non-goals

### Goals (this spec)

1. Move upstream `Aevatar.Tools.Cli/Frontend/` source into Aexon at `src/Aexon.Commands/Frontend/`.
2. Trim it to chat console + settings only. Drop workflow studio, scripts, gagent-types, config explorer.
3. Add `Workbench/` directory under `src/Aexon.Commands/`, populated by the design files.
4. Refactor the Frontend Vite project to multi-entry: chat + workbench, both real bundled outputs.
5. Refactor `AevatarWebHost` to take a webroot subdirectory parameter.
6. Add `aexon aevatar chat` subcommand. Repoint `aexon aevatar web` at the workbench webroot.
7. Old `wwwroot/aevatar/*` deleted; new build outputs at `wwwroot/aevatar-chat/` and `wwwroot/aevatar-workbench/`.
8. Update `scripts/reinstall.sh` to build the frontend before packing.

### Non-goals (this spec)

- Wiring Workbench to the real Aevatar backend. The Workbench renders against `js/demo-data.js` only. Real wiring is a future spec; see §10 for backend gap analysis.
- TSX migration of Workbench files. Stays as `.jsx` written in `React.createElement(...)` style (the design's original form). Vite handles JSX without TS just fine.
- Any change to the underlying chat backend protocol or `AevatarChatClient`.
- A "second binding" of the existing chat experience inside the Workbench's Invoke step (the Workbench has its own playground; both can coexist).

## 4. User experience

### `aexon aevatar chat [--port N] [--no-browser]`

- Default port: **6688** (sticky with current `aevatar web` to avoid breaking existing muscle memory; old command is repurposed below).
- Opens the trimmed chat React bundle in the browser.
- Sidebar: Console icon + dark-mode toggle + Settings icon. Nothing else.
- Console page: `<ScopePage />` from upstream — the existing chat surface, with NyxID auth, chat history, tool approval, streaming.
- Settings page: both runtime (base URL) and cloud-config / LLM tabs preserved.
- Banner: `aexon aevatar chat — chat-only mode (workflow / scripts / explorer surfaces removed)`.

### `aexon aevatar web [--port N] [--no-browser]`

- Default port: **6689** (avoids collision with `chat` if both are running).
- Opens the Service Workbench prototype in the browser.
- Banner: `aexon aevatar web — Service Workbench (prototype, demo data only)`.
- All interactions are local-only against `demo-data.js`. The `/api/*` reverse proxy is wired through the host (consistent host behavior across both subcommands), but the Workbench prototype does not call it.

## 5. Architecture

### 5.1 Directory layout

```
src/Aexon.Commands/
├── Aexon.Commands.csproj         (modified: register Workbench static content)
├── AevatarCommand.cs              (modified: add chat subcommand)
├── AevatarWebHost.cs              (modified: webroot/port parameterization)
│
├── Frontend/                      (new — copied from upstream Frontend/, trimmed)
│   ├── package.json               (deps cut: xyflow, monaco-editor, monaco-react, tanstack-virtual)
│   ├── vite.config.ts             (multi-entry: chat + workbench)
│   ├── index.html                 (chat entry; existing)
│   ├── workbench.html             (workbench entry; new)
│   ├── tsconfig.json
│   ├── tailwind.config.js         (kept; chat uses tailwind classes)
│   ├── postcss.config.js
│   ├── src/                       (chat source — trimmed)
│   │   ├── main.tsx
│   │   ├── App.tsx                (heavily trimmed: ~5600 → est. ~1500 lines)
│   │   ├── api.ts                 (chat/auth-only subset)
│   │   ├── auth/                  (kept entirely)
│   │   ├── runtime/               (kept entirely — chat surface)
│   │   ├── index.css
│   │   └── vite-env.d.ts
│   └── workbench/                 (new — design files copied verbatim)
│       ├── main.jsx               (small entry that imports React + DOM and the design's app.jsx)
│       ├── styles/tokens.css
│       └── js/
│           ├── demo-data.js
│           ├── atoms.jsx
│           ├── chrome.jsx
│           ├── build.jsx
│           ├── bind.jsx
│           ├── invoke.jsx
│           ├── observe.jsx
│           ├── tweaks.jsx
│           └── app.jsx
│
└── wwwroot/                       (build outputs — checked into git, packed into nupkg)
    ├── aevatar-chat/
    │   ├── index.html
    │   ├── app.js                 (~300-400 KB target)
    │   └── app.css
    └── aevatar-workbench/
        ├── workbench.html  (or  index.html)
        ├── app.js
        └── app.css
```

Files **deleted** from upstream as part of the trim:
- `src/ScriptsStudio.tsx`
- `src/scripts-studio/` (whole directory)
- `src/config-explorer/` (whole directory)
- `src/runtime/GAgentPage.tsx`
- `src/runtime/GAgentTab.tsx`
- `src/studio.ts`
- `src/api.chrono-storage.test.ts` if it tests removed surface (verify; remove if so)

Files **deleted** from current Aexon repo:
- `src/Aexon.Commands/wwwroot/aevatar/*` (all five files)

### 5.2 Vite multi-entry build

`Frontend/vite.config.ts` produces two independent bundles in one `npm run build` pass:

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'path';

export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    cssCodeSplit: false,
    rollupOptions: {
      input: {
        chat: resolve(__dirname, 'index.html'),
        workbench: resolve(__dirname, 'workbench.html'),
      },
      output: {
        // Each entry's JS lands in its own subdirectory under wwwroot/.
        // Asset placement is handled by the post-build relocate script
        // (see implementation note below) — Rollup's per-entry asset routing
        // in multi-input HTML mode is unreliable, so we let it dump into a
        // staging dir and move during build.
        entryFileNames: 'aevatar-[name]/app.js',
        chunkFileNames: 'aevatar-[name]/[name]-[hash].js',
        assetFileNames: 'staging/[name][extname]',
      },
    },
  },
});
```

Implementation note: getting `index.html` and `workbench.html` to land at `wwwroot/aevatar-chat/index.html` and `wwwroot/aevatar-workbench/index.html` may need a small post-build move step (Rollup's input-name → output-html-path mapping in multi-entry mode is awkward). The post-build script can be a 5-line `node` snippet inside `package.json`'s `build` script, e.g. `"build": "tsc -b && vite build && node scripts/relocate-html.js"`. Acceptable cost.

### 5.3 Workbench module wiring

The design uses three globals: React UMD, ReactDOM UMD, Babel-standalone. Under Vite, none of those CDN globals are needed. The shim is minimal — a new `Frontend/workbench/main.jsx` entry:

```jsx
// workbench/main.jsx
import React from 'react';
import ReactDOM from 'react-dom/client';
import { useState, useEffect } from 'react';

// Make `React`, `ReactDOM`, and the React hooks available as globals to the
// design's JSX files, which were written to assume Babel-standalone + UMD.
window.React = React;
window.ReactDOM = ReactDOM;
window.useState = useState;
window.useEffect = useEffect;

// Demo fixtures attach to window.DEMO inside the IIFE.
import './js/demo-data.js';
import './styles/tokens.css';

// Component definitions (each file declares its components on the global scope).
import './js/atoms.jsx';
import './js/chrome.jsx';
import './js/build.jsx';
import './js/bind.jsx';
import './js/invoke.jsx';
import './js/observe.jsx';
import './js/tweaks.jsx';
import './js/app.jsx';   // app.jsx contains the ReactDOM.createRoot(...).render(...) call
```

`workbench.html` is a near-verbatim copy of `Service Workbench.html`, but with the `<script>` tags replaced by `<script type="module" src="/workbench/main.jsx"></script>` and the CDN script tags removed.

The design's JSX files use `React.createElement(...)` (not literal JSX syntax), so Vite needs no JSX transform on them — they parse as plain JS. (Spot-check during implementation; if any file does use literal JSX, Vite handles it for free under the React plugin.) Either way, no Babel-standalone runtime ships to the browser.

### 5.4 Host changes (`AevatarWebHost.cs`)

```csharp
public static async Task RunAsync(
    int port,
    string apiBaseUrl,
    string webRootSubdir,            // NEW — "aevatar-chat" or "aevatar-workbench"
    bool noBrowser,
    CancellationToken cancellationToken)
{
    _currentApiBaseUrl = apiBaseUrl.TrimEnd('/');
    var webRootPath = ResolveWebRootPath(webRootSubdir);
    // ... rest unchanged ...
}

private static string ResolveWebRootPath(string subdir)
{
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.Combine(baseDir, "wwwroot", subdir),
        Path.GetFullPath(Path.Combine(baseDir, $"../../../../Aexon.Commands/wwwroot/{subdir}")),
    };
    return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.html"))) ?? candidates[0];
}
```

`/api/*` reverse-proxy, SSE pass-through, port-conflict recovery, and `/auth/callback` fallback all stay identical for both subdirs. Workbench just doesn't exercise the proxy.

### 5.5 Command changes (`AevatarCommand.cs`)

Extend the dispatch switch:

```csharp
case "chat":
    await RunChatWebAsync(rest, context);
    return;

case "web":
    await RunWebAsync(rest, context);   // existing, but now passes "aevatar-workbench"
    return;
```

Two thin private methods that share `ParseWebFlags` (renamed to be subdir-agnostic) and call into `AevatarWebHost.RunAsync` with their respective `(defaultPort, subdir)` pair.

`PrintUsage` adds:
```
/aevatar chat [--port N] [--no-browser]   start chat-only web UI (default port 6688)
/aevatar web  [--port N] [--no-browser]   start Service Workbench prototype (default port 6689)
```

### 5.6 Build pipeline

- **Workbench static assets**: handled entirely by Vite (§5.2 / §5.3). No special csproj rule.
- **Chat React bundle**: handled entirely by Vite (§5.2). No special csproj rule.
- **Existing csproj rule** (`<Content Include="wwwroot\**\*">`) already packs the build output into the nupkg. No change needed there.
- **`scripts/reinstall.sh`**: prepend a `(cd src/Aexon.Commands/Frontend && npm ci && npm run build)` step before `dotnet pack`. If `node` / `npm` is missing, fail loudly.
- **`dotnet build` alone** does NOT run `npm`. We deliberately avoid coupling. If a developer runs raw `dotnet build` without first running the Vite build, the bundles fall back to whatever's checked in to `wwwroot/`.
- **Git policy**: build outputs `wwwroot/aevatar-chat/*` and `wwwroot/aevatar-workbench/*` are checked in to git (current convention; no change). `Frontend/node_modules/` is gitignored. `Frontend/dist/` is N/A — Vite outputs straight to `wwwroot/`.

### 5.7 Long-term architectural decisions

These are explicit picks against alternatives, captured for future maintainers:

1. **Vite multi-entry over CDN + Babel-standalone for Workbench.** Babel-in-browser is an anti-pattern for production: parses JSX every page load, ships a 700 KB compiler, and depends on a CDN. Vite gives real bundling, tree-shake, minification, offline-correctness, and a clean TSX migration path for Phase 3 (just rename `.jsx` → `.tsx` and add types).
2. **One Vite project, two entries — not two separate Vite projects.** Shared deps (React, ReactDOM, lucide-react if/when Workbench uses it) install once. Single `npm run build`. One `node_modules/`.
3. **Workbench source at `Frontend/workbench/`, not a sibling of `Frontend/`.** Keeps everything frontend-related in one tree, one toolchain, one tsconfig (Workbench gets a permissive `allowJs` / no-check carve-out).
4. **Static `wwwroot/` directories per subcommand, not URL-prefixed routes on a shared host.** Lets each subcommand have its own web root, port, and lifecycle. Avoids cross-contamination — if Workbench grows route handlers later, it doesn't accidentally intercept chat's routes.
5. **Settings page kept whole (runtime + cloud-config / LLM).** Trimming the LLM tab would duplicate Aexon CLI's NyxID-LLM onboarding flow with extra delete/restore churn; the LLM tab is harmless to keep.
6. **No code sharing between chat and Workbench yet.** Workbench may eventually want NyxID auth, scope picker, or AGUI render components from the chat side. Address that in Phase 3 by promoting shared bits to a `Frontend/src/shared/` directory; not worth doing pre-emptively.

## 6. Trim plan — chat bundle

### 6.1 `App.tsx` changes

```tsx
// Line 141 — was:
type WorkspacePage = 'studio' | 'scripts' | 'gagents' | 'explorer' | 'console' | 'settings';
// becomes:
type WorkspacePage = 'console' | 'settings';

const WORKSPACE_PAGE_VALUES: WorkspacePage[] = ['console', 'settings'];
const NON_SETTINGS_WORKSPACE_PAGE_VALUES: NonSettingsWorkspacePage[] = ['console'];
```

```tsx
// Line ~447 — readStoredWorkspacePage default:
return isWorkspacePage(raw) ? raw : 'console';   // was 'studio'
```

Sidebar (lines 3426-3485): delete the four "Service Sources" `<RailButton>`s (Workflow Studio, Script Studio, GAgent Types, Explorer) and the divider above them. Keep brand mark, Console button, dark-mode toggle, Settings button.

Render branches (lines 3500-3525): keep only `'console' → <ScopePage />` and `'settings' → <SettingsView />`. Delete the `'scripts'`, `'explorer'`, `'gagents'` branches.

Drop all imports / state / handlers tied to deleted surfaces:
- `import ScriptsStudio from './ScriptsStudio';` and all references
- `import ConfigExplorerPage from './config-explorer/ConfigExplorerPage';` and all references
- `import GAgentPage from './runtime/GAgentPage';` and all references
- All workflow-studio code in `App.tsx`: `WorkflowNodeCard`, `studioView` state, `executionTrace`, `decorateNodes`, `decorateEdges`, every `<ReactFlow>` JSX, every `applyNodeChanges` / `applyEdgeChanges` callback, all of `from './studio'` imports
- All xyflow imports at the top

Estimated post-trim `App.tsx`: ~1500 lines (down from 5625).

### 6.2 Files deleted entirely

- `src/ScriptsStudio.tsx`
- `src/scripts-studio/` (whole directory)
- `src/config-explorer/` (whole directory)
- `src/runtime/GAgentPage.tsx`
- `src/runtime/GAgentTab.tsx`
- `src/studio.ts`

### 6.3 `api.ts` trim

Keep (used by chat / settings / auth):
- All `/api/scopes/{id}/nyxid-chat/*` endpoints (createConversation, listConversations, streamMessage, deleteConversation, approveToolCall)
- All `/api/scopes/{id}/chat-history/*` endpoints
- All `/api/auth/*` endpoints
- `/api/_proxy/runtime-url`
- `/api/scopes/gagent-types` (used by settings or NyxID gagent-type picker — verify; remove if unused)
- LLM provider / cloud-config endpoints (settings tab needs them)

Delete (workflow / script / connector / explorer):
- `/api/workspace/*`
- `/api/connectors*`
- `/api/explorer/*`
- `/api/services/*` (workflow-side surface; chat doesn't call this)
- `/api/editor/*`, `/api/scripts/*` (other than chat-related)
- `/api/workflow/draft-run`
- `/api/executions`
- `/api/roles*`

### 6.4 `package.json` deps

**Remove:**
- `@xyflow/react`
- `@monaco-editor/react`
- `monaco-editor`
- `@tanstack/react-virtual`

**Keep:**
- `@chenglou/pretext` (chat pretext rendering)
- `react`, `react-dom`
- `lucide-react`

Run `npm install` after edits to refresh `package-lock.json`.

### 6.5 Test trim

- Verify `src/api.chrono-storage.test.ts`: keep if it tests chat/settings APIs, delete if it tests deleted surface.
- `src/runtime/chatContent.test.ts` — keep (chat-content rendering tests).
- Add a smoke test that builds the trimmed bundle and asserts `wwwroot/aevatar-chat/app.js` exists and is < 600 KB.

## 7. Workbench prototype port

Drop the design files into `Frontend/workbench/` largely verbatim:

- Copy `Service Workbench.html` → `Frontend/workbench.html`. Remove `<script src="https://unpkg.com/...react.../*">` and `<script src=".../babel.../*">` and `<script src="js/...">` tags. Replace with `<script type="module" src="/workbench/main.jsx"></script>` per §5.3.
- Copy `js/{atoms,chrome,build,bind,invoke,observe,tweaks,app}.jsx` and `js/demo-data.js` to `Frontend/workbench/js/` unchanged.
- Copy `styles/tokens.css` to `Frontend/workbench/styles/tokens.css` unchanged.
- Skip `Aevatar Service Workbench (standalone).html` (1.4 MB inlined version) — we don't need it.
- Skip the `__bundler_thumbnail` `<template>` block in the HTML (was for design-tool-internal preview).

Verify that no JSX file references browser globals beyond `window.DEMO`, `window.TWEAKS`, `useState`, `useEffect`, `React`, `ReactDOM`. If something else (e.g., `React.Fragment`) is used, expose it from `main.jsx` similarly.

## 8. CSS / fonts

The design uses `JetBrains Mono` from Google Fonts and references a missing `AlibabaSans` font in CSS as a fallback chain. Vite handles the Google Fonts `<link>` in the HTML directly. No action.

The tokens.css file uses `data-accent`, `data-theme`, `data-density` attributes set at runtime — those work fine bundled.

## 9. Verification plan

1. `cd src/Aexon.Commands/Frontend && npm install && npm run build`
   - Both `wwwroot/aevatar-chat/{index.html,app.js,app.css}` and `wwwroot/aevatar-workbench/{index.html,app.js,app.css}` exist.
   - Chat bundle is < 600 KB (well under the 1.1 MB current).
2. `dotnet build` succeeds; `wwwroot/aevatar-{chat,workbench}/*` copied to `bin/Debug/net10.0/wwwroot/...`.
3. `aexon aevatar chat --no-browser` → host starts on 6688, fetching `http://localhost:6688/` returns chat `index.html`. Manually open in browser, verify NyxID login → chat works end-to-end against mainnet.
4. `aexon aevatar web --no-browser` → host starts on 6689, fetching `http://localhost:6689/` returns workbench `index.html`. Manually open: Build/Bind/Invoke/Observe steps render with demo data, tweaks panel works, no console errors.
5. `aexon aevatar chat` and `aexon aevatar web` running concurrently — no port conflict.
6. `dotnet pack` produces a nupkg containing both webroot subdirs under `tools/any/any/wwwroot/`.
7. After global `dotnet tool install`, both subcommands work from anywhere.
8. Existing `AevatarCommand` / `AevatarChatClient` test suites still pass (no behavioral change).

## 10. Phase 3 reference — backend gap analysis

Captured here so future spec authors don't have to re-do the survey. Source: agent run `2026-04-19`, against `~/Code/aevatar/src/`. **None of these gaps are in scope for this spec — they're for the future "wire the Workbench to real backend" spec.**

### Counts
- ✅ Supported: 18
- ⚠ Partial: 12
- ❌ Missing: 9
- ❓ Unclear: 1

### Biggest gaps (would block real wiring)

1. **AGUI custom-event surface incomplete.** `thinking`, `ctx.change` (delta), `handoff`, `retry` are not first-class proto types; some land in `WorkflowReasoningCustomPayload`, others nowhere. Either extend `agui_events.proto` or document a `CustomEvent.name` registry the frontend can dispatch on.
2. **No first-class Provenance metadata.** The Workbench's "honesty design" (live / delayed / partial / seeded / unavailable dots on every panel) requires a side-channel that doesn't exist. Adding it touches every read endpoint.
3. **No run-diff endpoint.** Observe step's "run compare vs. baseline" panel has no backend support. Sketch: `GET /runs/{a}:compare/{b}` diffs `runs/{id}/audit` projections + final state snapshots and labels rows (regression / new-risk / change / hand-off).
4. **Generic HIL approval submit.** Only `nyxid-chat`-specific `:approve` exists. Verify whether `runs/{id}:resume` or `:signal` accept the approval payload, or add `runs/{id}:respond`.
5. **Binding response is missing UI-required fields.** `invokeUrl`, `env`, `rateLimit`, `allowedOrigins`, `streaming(SSE/WS/AGUI)` flags are not on `ServiceBindingSpec`. Bind form cannot round-trip without them. Also no `:rotate` endpoint (only `:retire`).

### Easy wins (already there)

- Service / revision / binding / run / audit / draft-run / invoke / streaming-invoke / rollout-rollback all live in `platform/Aevatar.GAgentService.Hosting/Endpoints/` with consistent `/api/scopes/{scopeId}/...` routes — most of Build / Bind / Invoke can be wired directly.
- AGUI streaming pipeline is real (`AGUIEventChannel`, `AGUISseWriter`, `WorkflowExecutionAGUIEventProjector`) and `NyxIdChat` already uses it end-to-end — a working reference for the Workbench playground SSE consumer.
- HIL human-input request side is fully modeled (`HumanInputRequestEvent` carries `prompt` / `options` / timeout / variable_name) — render side can work today.

### Coverage by Workbench step

**Service list / sidebar**
- ✅ List services in scope: `platform/.../Hosting/Endpoints/ServiceEndpoints.cs` `MapGet("")` (`HandleListServicesAsync`)
- ✅ Per-service revision tracking: `ServiceEndpoints.cs` `MapGet("/{serviceId}/revisions")`; `ScopeServiceEndpoints.cs` `/services/{id}/revisions`
- ⚠ Binding state flag: derivable from `ScopeServiceEndpoints` `MapGet("/binding")` + `Governance/.../ServiceBindingEndpoints.cs` — no consolidated `ServiceListItemDto`
- ❌ Health / provenance dot: no first-class `Provenance` or `Health` field. Sketch: add `ServiceHealthSnapshot` projection and `/services/{id}/health`
- ✅ Last-run timestamp: derivable from `ScopeServiceEndpoints.cs` `MapGet("/services/{id}/runs")`
- ⚠ Owner / kind badge: `serviceId`/`kind` come from `HandleGetServiceAsync`; explicit `owner` field not visible — small read-model addition

**Build step**
- ✅ 3 build modes: `ScopeWorkflowEndpoints.cs`, `ScopeScriptEndpoints.cs`, `ScopeGAgentEndpoints.cs`
- ✅ Workflow DAG editor: `ScopeWorkflowEndpoints.cs` + `Aevatar.Studio.Hosting/Endpoints/WorkflowGenerateOrchestrator.cs`
- ✅ Script editor + TS diagnostics: `Aevatar.Studio.Hosting/Endpoints/ScriptEditorValidationService.cs` + `POST /api/scripts/validate`
- ✅ GAgent type registry: `ScopeGAgentEndpoints.cs` `MapGet("/gagent-types")`
- ❓ GAgent persistence option (Orleans grain / ephemeral): no flag exposed
- ✅ Dry-run / preview: `POST /workflow/draft-run`, `POST /gagent/draft-run`, `POST /api/scopes/{scopeId}/scripts/draft-run`
- ✅ Continue → Bind hand-off: pure UI

**Bind step**
- ⚠ Generate invoke URL: bindings exist via `ScopeServiceEndpoints.cs` `POST /services/{id}/bindings`; `invokeUrl` field not on response — add to `BindingDto`
- ⚠ NyxID bearer explainer / token fetch: auth side ready (`Aevatar.Authentication.Providers.NyxId`), no `/api/auth/token` issuer here — UI links out
- ⚠ Binding params (env, rate-limit, streaming, origins): `ServiceBindingSpec` accepts identity + binding kind; missing fields above
- ❌ cURL / Fetch / SDK snippets: pure frontend templating; nothing backend-side beyond invokeUrl
- ✅ Existing-bindings table: `ServiceBindingEndpoints.cs` `MapGet("/{serviceId}/bindings")`
- ⚠ Rotate / revoke: `:retire` exists; no `:rotate` — add `POST /bindings/{id}:rotate`
- ✅ Smoke test: same `POST /services/{id}/invoke/{endpointId}`

**Invoke step**
- ✅ Playground send/receive: `ScopeServiceEndpoints.cs` `POST /services/{id}/invoke/{endpointId}`
- ✅ Streaming response (SSE): `POST /services/{id}/invoke/{endpointId}:stream`, `POST /invoke/chat:stream`; SSE writer `Aevatar.Presentation.AGUI/AGUISseWriter.cs`
- ✅ Request history: `MapGet("/services/{id}/runs")`; `MapGet("/services/{id}/runs/{runId}/audit")`
- ❌ Saved playground requests: no persistence — sketch: `POST/GET /scopes/{id}/services/{id}/saved-requests`
- ✅ AGUI `run.start` / `run.finished` / `run.error`: `Aevatar.Presentation.AGUI/agui_events.proto`
- ⚠ AGUI `step.error`: `StepStartedEvent` / `StepFinishedEvent` exist; `step.error` derived from `RunError` or `StepFinished` failure status
- ⚠ AGUI `thinking`: not in proto; in `WorkflowReasoningCustomPayload` (`EventEnvelopeToWorkflowRunEventMapper.cs:431`) — frontend decodes `CustomEvent.payload`
- ✅ AGUI `tool.call` + `tool.result`: `ToolCallStartEvent`, `ToolCallEndEvent`
- ❌ AGUI `handoff`: not in proto, no custom payload — sketch: emit `CustomEvent{name:"handoff"}` from workflow fan-out
- ⚠ AGUI `ctx.change`: only `StateSnapshotEvent` (full snapshot). UI showing single keys would need `StateDeltaEvent` or `CustomEvent{name:"ctx.change"}`
- ❌ AGUI `retry`: not anywhere — sketch: emit `CustomEvent{name:"retry", attempt:N}` from `Aevatar.Workflow.Core` retry loops
- ✅ AGUI `human.request`: `HumanInputRequestEvent`
- ✅ 5 render modes: pure frontend
- ✅ Live metrics strip: derivable from event stream
- ⚠ Inline HIL approval submit: `NyxIdChat`-specific exists; generic submit may be `runs/{id}:resume` or `:signal` — verify

**Observe step**
- ✅ Run audit / replay: `ScopeServiceEndpoints.cs` `MapGet("/services/{id}/runs/{runId}/audit")`
- ❌ Run compare vs. baseline: no diff endpoint — sketch: `GET /runs/{a}:compare/{b}`
- ⚠ Human escalation playback: reconstructable from `runs/{id}/audit` + HIL events; no dedicated endpoint
- ⚠ Governance snapshot: `ServiceServingEndpoints.cs` covers `/serving`, `/deployments`, `/traffic`, `/rollouts`; no aggregate
- ✅ Rollback: `ServiceServingEndpoints.cs` `POST /{serviceId}/rollouts/{rolloutId}:rollback`
- ❌ Health & trust rail: no aggregate `/services/{id}/health` endpoint
- ❌ Provenance labels: no first-class metadata anywhere

**Cross-cutting**
- ✅ NyxID bearer auth: `Aevatar.Authentication.Providers.NyxId/AddNyxIdAuthentication`; `[RequireAuthorization]` on `ScopeServiceEndpoints.cs:37`
- ✅ Scope/team header propagation: `ScopeEndpointAccess.cs`
- ✅ AGUI event registry: `Aevatar.Presentation.AGUI/agui_events.proto`

## 11. Risks

- **Vite multi-entry HTML output paths.** Rollup's input-name → output-html path mapping in multi-entry mode requires either custom `entryFileNames` plumbing or a tiny post-build relocate script. Mitigation: implement and verify in the very first plan step; budget time.
- **Trim leaves dangling state in `App.tsx`.** Lots of `useState` + handlers reference deleted surfaces. Mitigation: trim type-first (TypeScript will surface every reference), then iterate. Don't trust grep alone.
- **Workbench design's `React.createElement` style assumes UMD globals (`React`, `useState`, `useEffect`).** Mitigation: `main.jsx` shim exposes them on `window` (§5.3). Verified by simply running the bundled output and checking for `ReferenceError`s.
- **Old `wwwroot/aevatar/*` referenced elsewhere.** Mitigation: grep the repo for `wwwroot/aevatar` and any path containing `aevatar/app.js` / `aevatar/index.html` before deletion.

## 12. Open questions

None at spec time. All "decide for me" items resolved by design § C and the long-term decisions in §5.7.
