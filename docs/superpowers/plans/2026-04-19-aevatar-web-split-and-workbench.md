# Aevatar web split + Service Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `aexon aevatar`'s web command into `chat` (trimmed upstream React bundle) and `web` (new Service Workbench prototype, demo data only). Replace `wwwroot/aevatar/*` with two scoped webroots: `aevatar-chat/` and `aevatar-workbench/`.

**Architecture:** Pull upstream `Aevatar.Tools.Cli/Frontend/` into `src/Aexon.Commands/Frontend/`. Trim it to chat console + settings only. Add the Workbench design files as a second Vite entry under `Frontend/src/workbench/`. Refactor `AevatarWebHost` to take a `webRootSubdir` parameter. Add `aexon aevatar chat` subcommand. Old `wwwroot/aevatar/*` deleted. Build pipeline: `npm run build` produces both bundles in one pass; `scripts/reinstall.sh` runs the npm step before `dotnet pack`.

**Tech Stack:** .NET 10 / ASP.NET Core (host), React 18 + TypeScript + Vite (Frontend), Tailwind CSS (chat), JSX prototype (Workbench).

**Spec:** `docs/superpowers/specs/2026-04-19-aevatar-web-split-and-workbench-design.md`

---

## Pre-task: Worktree

This plan creates a substantial cross-cutting change. **Before starting, create a worktree** (use `superpowers:using-git-worktrees`):

```bash
# from Aexon repo root
git worktree add -b feat/aevatar-web-split ../aexon-web-split dev
cd ../aexon-web-split
```

All subsequent paths are relative to the worktree root.

---

## Task 1: Pull upstream Frontend source into Aexon

Vendoring the upstream `Aevatar.Tools.Cli/Frontend/` wholesale before trimming. Verifies the source builds cleanly in our tree before we change anything.

**Files:**
- Create (copy): `src/Aexon.Commands/Frontend/` (entire dir from `~/Code/aevatar/tools/Aevatar.Tools.Cli/Frontend/`, excluding `node_modules/` and `dist/`)
- Modify: `.gitignore`

- [ ] **Step 1.1: Copy the upstream Frontend tree**

```bash
mkdir -p src/Aexon.Commands/Frontend
rsync -a --exclude=node_modules --exclude=dist --exclude=.vite \
  ~/Code/aevatar/tools/Aevatar.Tools.Cli/Frontend/ \
  src/Aexon.Commands/Frontend/
```

- [ ] **Step 1.2: Verify nothing accidentally vendored**

```bash
ls src/Aexon.Commands/Frontend/
# expect: index.html, package.json, package-lock.json, postcss.config.js,
#         src/, tailwind.config.js, tsconfig.json, tsconfig.node.json,
#         vite.config.js, vite.config.ts, vitest.config.ts
# must NOT see: node_modules/, dist/, tsconfig*.tsbuildinfo
```

If `tsconfig*.tsbuildinfo` slipped in (rsync might catch them), delete them:
```bash
find src/Aexon.Commands/Frontend -name '*.tsbuildinfo' -delete
```

- [ ] **Step 1.3: Add gitignore entries**

Append to `.gitignore` (create the section if Frontend/ entries don't already exist):

```
# Aexon.Commands frontend
src/Aexon.Commands/Frontend/node_modules/
src/Aexon.Commands/Frontend/dist/
src/Aexon.Commands/Frontend/.vite/
src/Aexon.Commands/Frontend/*.tsbuildinfo
```

- [ ] **Step 1.4: Install npm deps and verify upstream builds as-is**

```bash
cd src/Aexon.Commands/Frontend
npm ci
# If npm ci fails (no lockfile compatibility), fall back to:
#   npm install
```

Expected: install completes without errors.

- [ ] **Step 1.5: Run a baseline build (we throw away the output)**

```bash
cd src/Aexon.Commands/Frontend
# vite.config.ts currently outputs to ../wwwroot — that's fine,
# we'll restructure in Task 4. Build just to verify the source compiles.
npx vite build
```

Expected: build completes; output appears at `src/Aexon.Commands/wwwroot/{index.html,app.js,app.css}` (will be overwritten + restructured later).

- [ ] **Step 1.6: Discard the test build output**

```bash
# We'll regenerate after the trim. Restore the existing aevatar/ webroot for now.
git checkout -- src/Aexon.Commands/wwwroot/
```

- [ ] **Step 1.7: Commit**

```bash
git add src/Aexon.Commands/Frontend/ .gitignore
git status  # confirm only Frontend/ + .gitignore staged
git commit -m "$(cat <<'EOF'
feat(aevatar): vendor upstream Aevatar.Tools.Cli Frontend into Aexon

Verbatim copy of Aevatar.Tools.Cli/Frontend (sans node_modules, dist).
Will be trimmed in subsequent commits to chat-console + settings only.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Trim chat bundle — App.tsx structural surgery

The largest single edit. Reduces `App.tsx` from ~5600 to ~1500 lines by removing workflow studio, scripts, gagent type browser, config explorer code paths.

**Files:**
- Modify: `src/Aexon.Commands/Frontend/src/App.tsx`
- Delete: `src/Aexon.Commands/Frontend/src/ScriptsStudio.tsx`
- Delete: `src/Aexon.Commands/Frontend/src/scripts-studio/` (whole directory)
- Delete: `src/Aexon.Commands/Frontend/src/config-explorer/` (whole directory)
- Delete: `src/Aexon.Commands/Frontend/src/runtime/GAgentPage.tsx`
- Delete: `src/Aexon.Commands/Frontend/src/runtime/GAgentTab.tsx`
- Delete: `src/Aexon.Commands/Frontend/src/studio.ts`

- [ ] **Step 2.1: Trim WorkspacePage enum**

In `src/Aexon.Commands/Frontend/src/App.tsx`, locate line ~141:

```ts
type WorkspacePage = 'studio' | 'scripts' | 'gagents' | 'explorer' | 'console' | 'settings';
```

Replace with:

```ts
type WorkspacePage = 'console' | 'settings';
```

In the same area (lines ~142, ~149-150), replace:

```ts
type NonSettingsWorkspacePage = Exclude<WorkspacePage, 'settings'>;
const WORKSPACE_PAGE_VALUES: WorkspacePage[] = ['studio', 'scripts', 'gagents', 'explorer', 'console', 'settings'];
const NON_SETTINGS_WORKSPACE_PAGE_VALUES: NonSettingsWorkspacePage[] = ['studio', 'scripts', 'gagents', 'explorer'];
```

with:

```ts
type NonSettingsWorkspacePage = Exclude<WorkspacePage, 'settings'>;
const WORKSPACE_PAGE_VALUES: WorkspacePage[] = ['console', 'settings'];
const NON_SETTINGS_WORKSPACE_PAGE_VALUES: NonSettingsWorkspacePage[] = ['console'];
```

- [ ] **Step 2.2: Update default landing page**

In `App.tsx`, locate `readStoredWorkspacePage` (line ~440-450) and change the default from `'studio'` to `'console'`:

```ts
function readStoredWorkspacePage(): WorkspacePage {
  // ... existing guard logic ...
  return isWorkspacePage(raw) ? raw : 'console';   // was 'studio'
}
```

Same for `readStoredPreviousWorkspacePage` if it defaults to `'studio'`:

```ts
function readStoredPreviousWorkspacePage(): NonSettingsWorkspacePage {
  // ... existing guard logic ...
  return isNonSettingsWorkspacePage(raw) ? raw : 'console';   // was 'studio'
}
```

- [ ] **Step 2.3: Delete sidebar Service Sources + Storage rail buttons**

In `App.tsx`, locate the sidebar (`<aside className="studio-rail">`, around line 3426). Delete:

- The "Service Sources" comment + divider (`{/* ── Service Sources ── */}` block)
- `<RailButton ...Workflow Studio />`
- The conditional `{appContext.scriptsEnabled ? <RailButton ...Script Studio ...> : null}` block
- `<RailButton ...GAgent Types />`
- The "Storage" comment + divider (`{/* ── Storage ── */}` block)
- `<RailButton ...Explorer />`

The sidebar should now look like:

```tsx
<aside className="studio-rail">
  <div className="flex flex-col items-center gap-3">
    <div className="flex h-11 w-11 items-center justify-center overflow-hidden rounded-[14px] border border-black/10 bg-[#18181B]">
      <AevatarBrandMark size={44} />
    </div>
  </div>

  <div className="mt-auto flex flex-col items-center gap-3">
    <RailButton
      active={workspacePage === 'console'}
      label="Console"
      icon={<Globe size={18} />}
      onClick={() => setWorkspacePage('console')}
    />
    <RailButton
      active={settingsState.colorMode === 'dark'}
      label={settingsState.colorMode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
      icon={settingsState.colorMode === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
      onClick={() => { void toggleColorMode(); }}
    />
    <RailButton
      active={workspacePage === 'settings'}
      label="Settings"
      icon={<Settings size={18} />}
      onClick={() => openSettingsPage('runtime')}
    />
  </div>
</aside>
```

- [ ] **Step 2.4: Delete render branches for scripts / explorer / gagents**

In `App.tsx`, locate the page render switch (around line 3500). Remove the three branches:

- `workspacePage === 'scripts' ? <ScriptsStudio ... /> : ...`
- `workspacePage === 'explorer' ? <ConfigExplorerPage ... /> : ...`
- `workspacePage === 'gagents' ? <GAgentPage /> : ...`

The chain should now be:

```tsx
<main className="flex-1 min-w-0 flex flex-col">
  {storageWarning && workspacePage !== 'explorer' ? (
    // ...keep storage warning block, but the && check can be simplified
    // since 'explorer' is no longer a valid page; replace with `true`:
    null   // OR keep storageWarning logic if it applies to console/settings; verify use
  ) : null}
  {workspacePage === 'console' ? (
    <ScopePage />
  ) : workspacePage === 'settings' ? (
    // ... existing settings JSX (the long block starting at line ~3526) ...
  ) : null}
</main>
```

Note: the `storageWarning` block was guarded by `!== 'explorer'`. Since explorer is gone, audit whether `storageWarning` makes sense at all in the chat-only context — if it only fires for explorer storage, delete it.

- [ ] **Step 2.5: Delete the orphan source files**

```bash
cd src/Aexon.Commands/Frontend
rm -f src/ScriptsStudio.tsx
rm -rf src/scripts-studio
rm -rf src/config-explorer
rm -f src/runtime/GAgentPage.tsx
rm -f src/runtime/GAgentTab.tsx
rm -f src/studio.ts
```

- [ ] **Step 2.6: Strip orphan imports + unused state from App.tsx**

In `App.tsx`, delete every import that now resolves to nothing:

```ts
import ScriptsStudio from './ScriptsStudio';
import ConfigExplorerPage from './config-explorer/ConfigExplorerPage';
import GAgentPage from './runtime/GAgentPage';
// All imports from './studio' (a long named-import block ~lines 60-100)
```

Delete xyflow imports (top of file, lines ~3-20):

```ts
import {
  ReactFlow,
  Controls,
  Background,
  // ... entire xyflow import block
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
```

Delete unused lucide-react icons (only delete ones that grep can't find usages of after the trim — leave them in if uncertain).

Delete `WorkflowNodeCard` component definition entirely.

Delete state and handlers tied to workflow studio:
- `studioView` state and `setStudioView`
- `executionTrace`, `executionLogs`, `executionInteraction` state
- `nodes`, `edges`, `setNodes`, `setEdges`, `onNodesChange`, `onEdgesChange`, `onConnect`
- All `decorateNodes...`, `decorateEdges...`, `buildGraphFromWorkflow`, `applyConnectorDefaults`, `createNode`, `createEdge`, `findExecutionLogIndexForStep` calls
- All `<ReactFlow>` JSX blocks
- `nodeTypes` / `CATEGORY_ICONS` const if only used by workflow studio

This is a sweeping deletion. Use TypeScript as a safety net: each delete will surface the next reference to delete.

- [ ] **Step 2.7: Iteratively run the type-check and fix until clean**

```bash
cd src/Aexon.Commands/Frontend
npx tsc -b --noEmit
```

Expected on first run: many errors pointing to remaining references to deleted symbols. Fix them one at a time, deleting code that referred to the removed surface. Repeat until tsc reports no errors.

- [ ] **Step 2.8: Sanity check — read what's left in App.tsx**

```bash
wc -l src/Aexon.Commands/Frontend/src/App.tsx
# expected: ~1200-1700 lines (was 5625)
```

Open the file and skim. Confirm: no xyflow JSX, no Monaco refs, no scripts/explorer/gagents render branches.

- [ ] **Step 2.9: Commit**

```bash
git add src/Aexon.Commands/Frontend/
git commit -m "$(cat <<'EOF'
refactor(aevatar/frontend): trim chat bundle to console + settings

Removes Workflow Studio, Script Studio, GAgent Types, Config Explorer
surfaces from the upstream React bundle. WorkspacePage enum is now
'console' | 'settings'. Default landing page is 'console'.

Deleted files:
- src/ScriptsStudio.tsx, src/scripts-studio/, src/studio.ts
- src/config-explorer/
- src/runtime/{GAgentPage,GAgentTab}.tsx

App.tsx trimmed from ~5600 to ~1500 lines.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Trim chat bundle — api.ts and package.json

Drop API helpers and npm deps that the trimmed UI no longer references.

**Files:**
- Modify: `src/Aexon.Commands/Frontend/src/api.ts`
- Modify: `src/Aexon.Commands/Frontend/package.json`
- Modify: `src/Aexon.Commands/Frontend/package-lock.json` (regenerated)

- [ ] **Step 3.1: Trim api.ts to chat / auth / settings endpoints**

Open `src/Aexon.Commands/Frontend/src/api.ts`. Keep these export groups:

- `nyxidChatApi` (or whatever exports the `/scopes/{id}/nyxid-chat/*` endpoints): `createConversation`, `listConversations`, `streamMessage`, `deleteConversation`, `approveToolCall`
- `chatHistoryApi`: `getIndex`, `getConversation`, `saveConversation`, `deleteConversation`
- `authApi`: `/api/auth/*` and `/api/_proxy/runtime-url` getters/setters
- LLM provider / `cloud-config` / `user-config` exports needed by settings page (verify against settings JSX in App.tsx)
- `gagent-types` API helper IF settings page (cloud-config) references gagent types — otherwise delete

Delete:
- workflow / `workspace/workflows` / `workflow/draft-run` helpers
- connectors / `connectors/draft` / `connectors/import`
- scripts editor / `editor/*` / `scripts/validate` / `scripts/generator`
- config explorer / `explorer/*`
- roles / `roles/draft` / `roles/import`
- executions / playground

Use grep to verify deletions don't break anything before doing them:

```bash
cd src/Aexon.Commands/Frontend
grep -rn 'workflowApi\|connectorsApi\|scriptsApi\|explorerApi' src/
# expected: no matches in App.tsx after Task 2 trim
```

- [ ] **Step 3.2: Run type-check to confirm trim is clean**

```bash
npx tsc -b --noEmit
```

Expected: no errors.

- [ ] **Step 3.3: Trim package.json runtime deps**

Open `src/Aexon.Commands/Frontend/package.json`. From `dependencies`, remove:

```json
"@monaco-editor/react": "^4.7.0",
"@tanstack/react-virtual": "^3.13.23",
"@xyflow/react": "^12.10.2",
"monaco-editor": "^0.55.1",
```

Keep: `@chenglou/pretext`, `lucide-react`, `react`, `react-dom`.

Don't touch `devDependencies`.

- [ ] **Step 3.4: Reinstall to refresh lock file**

```bash
cd src/Aexon.Commands/Frontend
rm -rf node_modules
npm install
```

Expected: install succeeds; `package-lock.json` updated.

- [ ] **Step 3.5: Build to verify**

```bash
npx vite build
```

Expected: build succeeds. Note the bundle size in console output — should be < 600 KB for `app.js` (was ~1.1 MB).

- [ ] **Step 3.6: Restore wwwroot (will be properly handled in Task 4)**

```bash
git checkout -- src/Aexon.Commands/wwwroot/
```

- [ ] **Step 3.7: Commit**

```bash
git add src/Aexon.Commands/Frontend/src/api.ts \
        src/Aexon.Commands/Frontend/package.json \
        src/Aexon.Commands/Frontend/package-lock.json
git commit -m "$(cat <<'EOF'
refactor(aevatar/frontend): trim api.ts + drop unused npm deps

Removes workflow / connector / scripts / explorer / role / execution
API helpers from api.ts. Drops @xyflow/react, @monaco-editor/react,
monaco-editor, @tanstack/react-virtual from runtime deps.

Bundle estimate after trim: ~300-500 KB (was ~1.1 MB).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Add Workbench design files as second Vite entry

Drop the design's HTML / JSX / CSS into `Frontend/src/workbench/`, restructure Vite for multi-entry, add post-build relocator.

**Files:**
- Create: `src/Aexon.Commands/Frontend/workbench.html`
- Create: `src/Aexon.Commands/Frontend/src/workbench/main.jsx`
- Create: `src/Aexon.Commands/Frontend/src/workbench/styles/tokens.css` (copy)
- Create: `src/Aexon.Commands/Frontend/src/workbench/js/{atoms,chrome,build,bind,invoke,observe,tweaks,app}.jsx` (copy)
- Create: `src/Aexon.Commands/Frontend/src/workbench/js/demo-data.js` (copy)
- Create: `src/Aexon.Commands/Frontend/scripts/relocate-html.js`
- Modify: `src/Aexon.Commands/Frontend/vite.config.ts`
- Modify: `src/Aexon.Commands/Frontend/package.json` (build script)

- [ ] **Step 4.1: Verify the design bundle is still on disk**

```bash
ls /tmp/aexon-design/aevatar-console/project/
# expected: js/, styles/, Service Workbench.html, Aevatar Service Workbench (standalone).html
```

If it's not there, re-extract from the original design URL or get it from the prior conversation's persisted output.

- [ ] **Step 4.2: Copy the design source files into the Frontend tree**

```bash
mkdir -p src/Aexon.Commands/Frontend/src/workbench/js
mkdir -p src/Aexon.Commands/Frontend/src/workbench/styles

cp /tmp/aexon-design/aevatar-console/project/styles/tokens.css \
   src/Aexon.Commands/Frontend/src/workbench/styles/tokens.css

cp /tmp/aexon-design/aevatar-console/project/js/{atoms,chrome,build,bind,invoke,observe,tweaks,app}.jsx \
   src/Aexon.Commands/Frontend/src/workbench/js/

cp /tmp/aexon-design/aevatar-console/project/js/demo-data.js \
   src/Aexon.Commands/Frontend/src/workbench/js/
```

- [ ] **Step 4.3: Create the Workbench entry HTML**

Write `src/Aexon.Commands/Frontend/workbench.html`:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Aevatar · Service Workbench</title>
<meta name="viewport" content="width=1400">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;600&display=swap" rel="stylesheet">
<style>
  @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.35} }
</style>
</head>
<body>
<div id="root"></div>
<script type="module" src="/src/workbench/main.jsx"></script>
</body>
</html>
```

(The design's `<template id="__bundler_thumbnail">` block is intentionally dropped — it was for the design tool's preview thumbnail and isn't useful here.)

- [ ] **Step 4.4: Create the Workbench main entry shim**

Write `src/Aexon.Commands/Frontend/src/workbench/main.jsx`:

```jsx
import React, { useState, useEffect } from 'react';
import ReactDOM from 'react-dom/client';

// Bridge UMD-style globals expected by the design's JSX files
// (which were originally written for React UMD + Babel-standalone).
window.React = React;
window.ReactDOM = ReactDOM;
window.useState = useState;
window.useEffect = useEffect;

// Demo fixtures attach to window.DEMO via IIFE.
import './js/demo-data.js';
import './styles/tokens.css';

// Component definitions (each file exports nothing; they declare
// components on window or via top-level function decls).
import './js/atoms.jsx';
import './js/chrome.jsx';
import './js/build.jsx';
import './js/bind.jsx';
import './js/invoke.jsx';
import './js/observe.jsx';
import './js/tweaks.jsx';
// app.jsx contains ReactDOM.createRoot(...).render(...) — must be last.
import './js/app.jsx';
```

- [ ] **Step 4.5: Create the post-build HTML relocator**

Write `src/Aexon.Commands/Frontend/scripts/relocate-html.js`:

```js
#!/usr/bin/env node
// After `vite build`, Rollup emits index.html and workbench.html at the
// root of outDir, and per-entry JS/CSS into aevatar-{chat,workbench}/
// subdirs (per vite.config.ts entryFileNames). This script:
//   1. moves index.html -> aevatar-chat/index.html
//   2. moves workbench.html -> aevatar-workbench/index.html
//   3. rewrites the asset URLs in each so they resolve under the subdir
//   4. moves any staging/ assets to their owning subdir

const fs = require('fs');
const path = require('path');

const wwwroot = path.resolve(__dirname, '..', '..', 'wwwroot');

function moveAndRewrite(srcHtml, destDir, entryName) {
  const srcPath = path.join(wwwroot, srcHtml);
  if (!fs.existsSync(srcPath)) {
    throw new Error(`relocate: ${srcPath} not found`);
  }
  let html = fs.readFileSync(srcPath, 'utf8');

  // Vite emits asset paths like "/aevatar-chat/app.js". When served from
  // the per-subcommand webroot (host serves wwwroot/aevatar-chat as `/`),
  // those paths must become "/app.js" — strip the subdir prefix.
  const subdirPrefix = `/aevatar-${entryName}/`;
  html = html.replaceAll(subdirPrefix, '/');

  const destPath = path.join(wwwroot, destDir, 'index.html');
  fs.mkdirSync(path.dirname(destPath), { recursive: true });
  fs.writeFileSync(destPath, html);
  fs.unlinkSync(srcPath);
  console.log(`relocate: ${srcHtml} -> ${destDir}/index.html`);
}

moveAndRewrite('index.html', 'aevatar-chat', 'chat');
moveAndRewrite('workbench.html', 'aevatar-workbench', 'workbench');

// Move any asset that landed in staging/ to whichever subdir its
// referencing HTML lives in. For simplicity, move everything in staging/
// to BOTH subdirs (Vite usually only emits .css here, and shared assets
// are fine to duplicate).
const staging = path.join(wwwroot, 'staging');
if (fs.existsSync(staging)) {
  for (const f of fs.readdirSync(staging)) {
    const src = path.join(staging, f);
    for (const dest of ['aevatar-chat', 'aevatar-workbench']) {
      fs.copyFileSync(src, path.join(wwwroot, dest, f));
    }
    fs.unlinkSync(src);
  }
  fs.rmdirSync(staging);
  console.log('relocate: staging/ flushed');
}
```

Make it executable:

```bash
chmod +x src/Aexon.Commands/Frontend/scripts/relocate-html.js
```

- [ ] **Step 4.6: Refactor vite.config.ts to multi-entry**

Replace `src/Aexon.Commands/Frontend/vite.config.ts` contents with:

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'path';

declare const process: {
  env: Record<string, string | undefined>;
};

const apiProxyTarget = process.env.AEVATAR_API_URL || '';

if (apiProxyTarget) {
  console.log(`[vite] API proxy: /api → ${apiProxyTarget}`);
}

export default defineConfig({
  plugins: [react()],
  base: '/',
  server: {
    ...(apiProxyTarget
      ? {
          proxy: {
            '/api': {
              target: apiProxyTarget,
              changeOrigin: true,
              secure: false,
              cookieDomainRewrite: { '*': '' },
              configure: (proxy: any) => {
                proxy.on('proxyReq', (proxyReq: any) => {
                  proxyReq.removeHeader('origin');
                  proxyReq.removeHeader('referer');
                });
                proxy.on('proxyRes', (proxyRes: any) => {
                  const setCookie = proxyRes.headers['set-cookie'];
                  if (setCookie) {
                    proxyRes.headers['set-cookie'] = setCookie.map((cookie: string) =>
                      cookie
                        .replace(/;\s*Secure/gi, '')
                        .replace(/;\s*SameSite=\w+/gi, '; SameSite=Lax'),
                    );
                  }
                });
              },
            },
          },
        }
      : {}),
  },
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
        // JS lands per-entry; assets dump into staging and are relocated
        // by scripts/relocate-html.js.
        entryFileNames: 'aevatar-[name]/app.js',
        chunkFileNames: 'aevatar-[name]/[name]-[hash].js',
        assetFileNames: 'staging/[name][extname]',
      },
    },
  },
});
```

Also remove the now-orphan `src/Aexon.Commands/Frontend/vite.config.js` (the `.js` sibling to the `.ts` config — the `.ts` one is the canonical):

```bash
rm -f src/Aexon.Commands/Frontend/vite.config.js
```

- [ ] **Step 4.7: Update package.json build script to call relocator**

In `src/Aexon.Commands/Frontend/package.json`, change the `build` script:

```json
"scripts": {
  "dev": "vite",
  "build": "tsc -b && vite build && node scripts/relocate-html.js",
  "test": "vitest run",
  "test:watch": "vitest",
  "lint": "eslint .",
  "preview": "vite preview"
}
```

- [ ] **Step 4.8: Build and verify both bundles**

```bash
cd src/Aexon.Commands/Frontend
npm run build
```

Expected: build succeeds, relocator prints two `relocate:` lines.

```bash
ls ../wwwroot/aevatar-chat/
# expected: index.html, app.js, app.css (maybe more chunks)

ls ../wwwroot/aevatar-workbench/
# expected: index.html, app.js, app.css

# staging/ should NOT exist anymore:
[ ! -d ../wwwroot/staging ] && echo "staging cleaned" || echo "STAGING LEFTOVER"
```

If `staging/` survived, debug the relocator. If asset paths in HTML look wrong, check Vite's output filenames vs. the `subdirPrefix` constant.

- [ ] **Step 4.9: Smoke-test the workbench HTML in a browser locally (optional but recommended)**

```bash
# from repo root, quick local server:
cd src/Aexon.Commands/wwwroot
npx http-server -p 8080 -c-1
# in browser: http://localhost:8080/aevatar-workbench/
# expected: Workbench loads, all 4 steps render with demo data, no console errors
# Ctrl+C to stop
```

If JSX files error with "React is not defined", the global bridge in `main.jsx` isn't loaded before the JSX files — verify import order in `main.jsx`.

- [ ] **Step 4.10: Commit**

```bash
git add src/Aexon.Commands/Frontend/ src/Aexon.Commands/wwwroot/
git status  # expect: new files in Frontend/, new files in wwwroot/aevatar-{chat,workbench}/
git commit -m "$(cat <<'EOF'
feat(aevatar): add Workbench design as second Vite entry

Drops the Service Workbench prototype (HTML + JSX + CSS) into
Frontend/src/workbench/ as a second Vite entry. Refactors vite.config.ts
to multi-entry; adds scripts/relocate-html.js post-build helper that
moves index.html and workbench.html into their respective wwwroot
subdirs and rewrites asset URLs.

Build outputs:
- wwwroot/aevatar-chat/{index,app.js,app.css}
- wwwroot/aevatar-workbench/{index,app.js,app.css}

Workbench is demo-data-only; no backend wiring (per spec § 4 / § 7).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Refactor AevatarWebHost for multiple webroot subdirs

Make the host accept which subdir of `wwwroot/` to serve. Existing call sites updated in Task 6.

**Files:**
- Modify: `src/Aexon.Commands/AevatarWebHost.cs`

- [ ] **Step 5.1: Add webRootSubdir parameter to RunAsync**

In `src/Aexon.Commands/AevatarWebHost.cs`, change the public signature:

```csharp
public static async Task RunAsync(
    int port,
    string apiBaseUrl,
    string webRootSubdir,
    bool noBrowser,
    CancellationToken cancellationToken)
{
    _currentApiBaseUrl = apiBaseUrl.TrimEnd('/');

    var webRootPath = ResolveWebRootPath(webRootSubdir);
    if (!File.Exists(Path.Combine(webRootPath, "index.html")))
    {
        Console.Error.WriteLine(
            $"  aevatar web: could not find frontend assets for '{webRootSubdir}'. Looked at: {webRootPath}");
        Console.Error.WriteLine(
            "  This tool must be installed as a packaged dotnet tool for the web UI to work.");
        return;
    }

    try
    {
        await StartOnceAsync(port, noBrowser, webRootPath, cancellationToken);
        return;
    }
    // ... rest of port-conflict handling unchanged ...
}
```

- [ ] **Step 5.2: Make ResolveWebRootPath subdir-aware**

In the same file, replace `ResolveWebRootPath()`:

```csharp
private static string ResolveWebRootPath(string subdir)
{
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.Combine(baseDir, "wwwroot", subdir),
        // dev-time fallback when running from the source checkout:
        Path.GetFullPath(Path.Combine(baseDir, $"../../../../Aexon.Commands/wwwroot/{subdir}")),
    };

    return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.html")))
           ?? candidates[0];
}
```

- [ ] **Step 5.3: Verify it still compiles in isolation**

```bash
dotnet build src/Aexon.Commands/Aexon.Commands.csproj
```

Expected: errors at the call site in `AevatarCommand.cs` (we haven't updated it yet — that's Task 6). Confirm the errors are exactly `RunAsync(int, string, bool, CancellationToken)` arity mismatches; no other errors.

- [ ] **Step 5.4: Commit (don't worry about the broken call site — Task 6 fixes it)**

We commit even though build is broken at the call site, because the next task immediately fixes it. This keeps logical changes separated.

```bash
git add src/Aexon.Commands/AevatarWebHost.cs
git commit -m "$(cat <<'EOF'
refactor(aevatar): parameterize AevatarWebHost with webRootSubdir

Adds webRootSubdir parameter so the host can serve different bundles
('aevatar-chat' or 'aevatar-workbench'). Build temporarily breaks at
the call site in AevatarCommand.cs — fixed in the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Add `aevatar chat` subcommand + retarget `aevatar web`

Wire the CLI dispatch to the new host signature with two subcommand-specific defaults.

**Files:**
- Modify: `src/Aexon.Commands/AevatarCommand.cs`

- [ ] **Step 6.1: Generalize ParseWebFlags to take a default port**

In `src/Aexon.Commands/AevatarCommand.cs`, change `ParseWebFlags` signature:

```csharp
private static (int Port, bool NoBrowser, string? Error) ParseWebFlags(string args, int defaultPort)
{
    var port = defaultPort;
    var noBrowser = false;
    // ... rest unchanged ...
}
```

- [ ] **Step 6.2: Update the existing RunWebAsync to point at the workbench webroot**

Replace the existing `RunWebAsync` body:

```csharp
private async Task RunWebAsync(string args, CommandContext context)
{
    const int defaultPort = 6689;
    const string webRootSubdir = "aevatar-workbench";

    var (port, noBrowser, error) = ParseWebFlags(args, defaultPort);
    if (error is not null)
    {
        context.WriteLine(error);
        context.WriteLine("  Usage: /aevatar web [--port <n>] [--no-browser]");
        return;
    }

    var settings = settingsStore.Load();
    var baseUrl = AevatarChatSettingsStore.ResolveBaseUrl(settings, @override: null);

    try
    {
        await AevatarWebHost.RunAsync(port, baseUrl, webRootSubdir, noBrowser, context.CancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        WriteError(context, ex);
    }
}
```

- [ ] **Step 6.3: Add RunChatWebAsync mirroring RunWebAsync but pointing at the chat webroot**

Add a new method right below `RunWebAsync`:

```csharp
private async Task RunChatWebAsync(string args, CommandContext context)
{
    const int defaultPort = 6688;
    const string webRootSubdir = "aevatar-chat";

    var (port, noBrowser, error) = ParseWebFlags(args, defaultPort);
    if (error is not null)
    {
        context.WriteLine(error);
        context.WriteLine("  Usage: /aevatar chat [--port <n>] [--no-browser]");
        return;
    }

    var settings = settingsStore.Load();
    var baseUrl = AevatarChatSettingsStore.ResolveBaseUrl(settings, @override: null);

    try
    {
        await AevatarWebHost.RunAsync(port, baseUrl, webRootSubdir, noBrowser, context.CancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        WriteError(context, ex);
    }
}
```

Note: there is naming friction here — the existing `chat` command name was traditionally not used because `/aevatar` itself is the chat REPL. We are adding `chat` as a web-launcher subcommand. Confirm with the user if this collides with any planned future use; current code uses `new`, `list`, `open`, `delete`, `config`, `send`, `help`, `web` — `chat` is free.

- [ ] **Step 6.4: Add `chat` to the dispatch switch**

In `ExecuteAsync`'s switch (around line 60-100), add a new case before the `default`:

```csharp
case "chat":
    await RunChatWebAsync(rest, context);
    return;
```

- [ ] **Step 6.5: Update PrintUsage**

In `PrintUsage`, replace the existing single web line with two:

```csharp
context.WriteLine("    /aevatar chat [--port N] [--no-browser]   start chat-only web UI (default port 6688)");
context.WriteLine("    /aevatar web  [--port N] [--no-browser]   start Service Workbench prototype (default port 6689)");
```

- [ ] **Step 6.6: Verify the project builds**

```bash
dotnet build src/Aexon.Commands/Aexon.Commands.csproj
```

Expected: build succeeds, no errors.

- [ ] **Step 6.7: Commit**

```bash
git add src/Aexon.Commands/AevatarCommand.cs
git commit -m "$(cat <<'EOF'
feat(aevatar): add 'chat' subcommand, retarget 'web' to Workbench

- /aevatar chat → chat-only web UI on port 6688 (default)
- /aevatar web  → Service Workbench prototype on port 6689 (default)

ParseWebFlags now takes a defaultPort parameter so each subcommand can
own its port number.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Add unit tests for ParseWebFlags

Lock in the new defaults with focused unit tests.

**Files:**
- Create: `tests/Aexon.Commands.Tests/AevatarWebFlagsTests.cs`

- [ ] **Step 7.1: Locate the existing test project**

```bash
find tests/ -maxdepth 2 -name '*.csproj' -path '*Commands*' 2>/dev/null
# expect: tests/Aexon.Commands.Tests/Aexon.Commands.Tests.csproj
# (or similar path — adapt below if different)
```

If no Commands test project exists yet, create one:

```bash
# only if missing:
mkdir -p tests/Aexon.Commands.Tests
# create a minimal csproj mirroring an existing test project's structure
```

(In practice, `AevatarCommandHelpersTests` already exists, so the project exists.)

```bash
# locate it:
find tests -name 'AevatarCommandHelpersTests.cs' -exec dirname {} \;
```

- [ ] **Step 7.2: Make ParseWebFlags internal-visible to the test project (if not already)**

Check `src/Aexon.Commands/AssemblyInfo.cs`:

```bash
cat src/Aexon.Commands/AssemblyInfo.cs
```

If `[InternalsVisibleTo("Aexon.Commands.Tests")]` is missing, add it. (`AevatarCommandHelpersTests` exists, so it's likely already there — verify.)

If we need to test private static `ParseWebFlags`, change its visibility to `internal static` in `AevatarCommand.cs`:

```csharp
internal static (int Port, bool NoBrowser, string? Error) ParseWebFlags(string args, int defaultPort)
```

- [ ] **Step 7.3: Write the test file**

Create `tests/Aexon.Commands.Tests/AevatarWebFlagsTests.cs` (adapt namespace to match existing tests):

```csharp
using Aexon.Commands;
using Xunit;

namespace Aexon.Commands.Tests;

public class AevatarWebFlagsTests
{
    [Fact]
    public void ParseWebFlags_DefaultPort_UsedWhenNoArgs()
    {
        var (port, noBrowser, error) = AevatarCommand.ParseWebFlags("", defaultPort: 6688);

        Assert.Equal(6688, port);
        Assert.False(noBrowser);
        Assert.Null(error);
    }

    [Fact]
    public void ParseWebFlags_DifferentDefault_HonorsCallerDefault()
    {
        var (port, _, _) = AevatarCommand.ParseWebFlags("", defaultPort: 6689);

        Assert.Equal(6689, port);
    }

    [Fact]
    public void ParseWebFlags_PortFlag_SeparateValue_OverridesDefault()
    {
        var (port, _, error) = AevatarCommand.ParseWebFlags("--port 7000", defaultPort: 6688);

        Assert.Equal(7000, port);
        Assert.Null(error);
    }

    [Fact]
    public void ParseWebFlags_PortFlag_EqualsValue_OverridesDefault()
    {
        var (port, _, error) = AevatarCommand.ParseWebFlags("--port=7100", defaultPort: 6688);

        Assert.Equal(7100, port);
        Assert.Null(error);
    }

    [Fact]
    public void ParseWebFlags_NoBrowserFlag_FlipsBoolean()
    {
        var (_, noBrowser, error) = AevatarCommand.ParseWebFlags("--no-browser", defaultPort: 6688);

        Assert.True(noBrowser);
        Assert.Null(error);
    }

    [Fact]
    public void ParseWebFlags_BothFlags_BothApplied()
    {
        var (port, noBrowser, error) = AevatarCommand.ParseWebFlags("--no-browser --port 7200", defaultPort: 6688);

        Assert.Equal(7200, port);
        Assert.True(noBrowser);
        Assert.Null(error);
    }

    [Fact]
    public void ParseWebFlags_InvalidPort_ReturnsError()
    {
        var (_, _, error) = AevatarCommand.ParseWebFlags("--port abc", defaultPort: 6688);

        Assert.NotNull(error);
        Assert.Contains("Invalid --port value", error);
    }

    [Fact]
    public void ParseWebFlags_PortOutOfRange_ReturnsError()
    {
        var (_, _, error) = AevatarCommand.ParseWebFlags("--port 70000", defaultPort: 6688);

        Assert.NotNull(error);
        Assert.Contains("Invalid --port value", error);
    }

    [Fact]
    public void ParseWebFlags_UnknownFlag_ReturnsError()
    {
        var (_, _, error) = AevatarCommand.ParseWebFlags("--purple", defaultPort: 6688);

        Assert.NotNull(error);
        Assert.Contains("Unknown flag", error);
    }
}
```

- [ ] **Step 7.4: Run the new tests; expect all green**

```bash
dotnet test tests/Aexon.Commands.Tests/ --filter 'FullyQualifiedName~AevatarWebFlagsTests'
```

Expected: 9 passed.

- [ ] **Step 7.5: Run the full test suite to confirm no regressions**

```bash
dotnet test
```

Expected: all tests pass.

- [ ] **Step 7.6: Commit**

```bash
git add tests/ src/Aexon.Commands/AssemblyInfo.cs src/Aexon.Commands/AevatarCommand.cs
# (the AssemblyInfo / AevatarCommand changes are only included if you
#  had to bump visibility for the test — otherwise just tests/)
git commit -m "$(cat <<'EOF'
test(aevatar): cover ParseWebFlags new default-port signature

Verifies both subcommand defaults (6688 chat, 6689 web), --port and
--port=N override forms, --no-browser flag, and error paths for
invalid / out-of-range / unknown flags.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Delete old wwwroot/aevatar/ bundle

Once `aevatar-chat/` and `aevatar-workbench/` are populated and working, the old single `aevatar/` directory is dead.

**Files:**
- Delete: `src/Aexon.Commands/wwwroot/aevatar/index.html`
- Delete: `src/Aexon.Commands/wwwroot/aevatar/app.js`
- Delete: `src/Aexon.Commands/wwwroot/aevatar/app.css`
- Delete: `src/Aexon.Commands/wwwroot/aevatar/codicon.ttf`
- Delete: `src/Aexon.Commands/wwwroot/aevatar/editor.worker-*.js`

- [ ] **Step 8.1: Confirm nothing references the old path**

```bash
# Search for hardcoded references to the old "aevatar" subdir (not "aevatar-chat" or "aevatar-workbench"):
grep -rn 'wwwroot/aevatar"' src/ tests/ 2>/dev/null | grep -v 'aevatar-chat\|aevatar-workbench'
grep -rn 'wwwroot[\\/]aevatar[\\/]' src/ tests/ 2>/dev/null | grep -v 'aevatar-chat\|aevatar-workbench'
# expected: no hits (the host now uses the parameterized subdir)
```

- [ ] **Step 8.2: Delete the old bundle**

```bash
rm -rf src/Aexon.Commands/wwwroot/aevatar/
```

- [ ] **Step 8.3: Build to confirm nothing broken**

```bash
dotnet build src/Aexon.Commands/Aexon.Commands.csproj
ls src/Aexon.Commands/bin/Debug/net10.0/wwwroot/
# expected: aevatar-chat/, aevatar-workbench/, config/  (no plain "aevatar/")
```

- [ ] **Step 8.4: Commit**

```bash
git add src/Aexon.Commands/wwwroot/
git status  # confirm only deletions
git commit -m "$(cat <<'EOF'
chore(aevatar): remove old wwwroot/aevatar/ single-bundle webroot

Replaced by wwwroot/aevatar-chat/ (chat console) and
wwwroot/aevatar-workbench/ (Service Workbench prototype).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Update reinstall.sh to build frontend before packing

Make `scripts/reinstall.sh` aware of the npm step so a fresh install gets fresh bundles.

**Files:**
- Modify: `scripts/reinstall.sh`

- [ ] **Step 9.1: Add the frontend build step before `dotnet pack`**

Open `scripts/reinstall.sh`. Find the line where `dotnet pack` is invoked. Insert a frontend build block just before it.

Locate the section that looks like:

```bash
echo "==> Cleaning previous package output"
rm -rf "$PACKAGE_OUTPUT"
mkdir -p "$PACKAGE_OUTPUT"
```

Insert before it (or just before the `dotnet pack` invocation, wherever it lives):

```bash
echo "==> Building Aexon.Commands frontend (chat + workbench)..."
FRONTEND_DIR="$REPO_ROOT/src/Aexon.Commands/Frontend"
if [[ ! -f "$FRONTEND_DIR/package.json" ]]; then
  echo "Frontend directory not found at $FRONTEND_DIR" >&2
  exit 1
fi

if ! command -v npm >/dev/null 2>&1; then
  echo "npm not found on PATH. Install Node.js (>= 20) to build the frontend." >&2
  exit 1
fi

(
  cd "$FRONTEND_DIR"
  if [[ -f package-lock.json ]]; then
    npm ci --ignore-scripts
  else
    npm install --ignore-scripts
  fi
  npm run build
)
echo "==> Frontend build complete."
```

- [ ] **Step 9.2: Run the script end-to-end**

```bash
./scripts/reinstall.sh
```

Expected: frontend build runs (Vite output visible), then dotnet pack, then the global tool reinstalls.

If `npm ci` fails on lockfile mismatch, the script falls back to `npm install` automatically per the conditional.

- [ ] **Step 9.3: Verify the global tool now has both subcommands**

```bash
aexon /aevatar chat --help 2>&1 | head -5
aexon /aevatar web --help 2>&1 | head -5
```

(If `--help` for these subcommands isn't separately implemented, just running them with an unknown flag will print usage. Adapt the verification.)

A more reliable check: invoke `/aevatar help` and confirm the printed usage now lists both `chat` and `web`:

```bash
aexon /aevatar help
# expected to include:
#   /aevatar chat [--port N] [--no-browser]   start chat-only web UI ...
#   /aevatar web  [--port N] [--no-browser]   start Service Workbench prototype ...
```

- [ ] **Step 9.4: Commit**

```bash
git add scripts/reinstall.sh
git commit -m "$(cat <<'EOF'
chore(scripts): build frontend before dotnet pack in reinstall

The Aexon.Commands frontend (chat + Workbench) must be built via
npm before the dotnet tool can pack a complete nupkg with the
wwwroot/aevatar-{chat,workbench}/ subdirs populated.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: End-to-end smoke test

Manual verification that both subcommands actually work against a real (or local) backend.

**Files:** None (verification only).

- [ ] **Step 10.1: Smoke test `aexon aevatar chat`**

```bash
aexon /aevatar chat --no-browser
```

Expected: host banner prints with `Web UI: http://localhost:6688`. In a separate shell or browser:

```bash
curl -s http://localhost:6688/ | head -3
# expected: <!DOCTYPE html>...  (the chat index.html)
```

Open the URL in a real browser. Verify:
- Page loads, no JS console errors
- Sidebar has only Console + dark mode toggle + Settings (no Workflow Studio etc.)
- NyxID login prompt appears (or saved session resumes)
- Settings page reachable from gear icon, both Runtime + LLM tabs render

Stop with Ctrl+C.

- [ ] **Step 10.2: Smoke test `aexon aevatar web`**

```bash
aexon /aevatar web --no-browser
```

Expected: host banner prints with `Web UI: http://localhost:6689`. Open in browser.

Verify:
- Workbench page loads, no JS console errors
- Service list (sidebar) shows the demo services
- Build / Bind / Invoke / Observe step nav works
- Each step's panels render with demo data
- Tweaks panel opens and theme/density toggles work

Stop with Ctrl+C.

- [ ] **Step 10.3: Concurrent run check**

In two terminals:

```bash
# Terminal 1:
aexon /aevatar chat --no-browser

# Terminal 2:
aexon /aevatar web --no-browser
```

Both should run simultaneously without port conflict. Visit both URLs.

- [ ] **Step 10.4: Document the test outcome**

If everything works, no commit needed (this task is verification).

If you find regressions, file them as separate fixup commits. Don't roll the fix into an unrelated commit.

---

## Self-Review Notes

After writing this plan, I checked it against the spec:

- **Spec § 4 user experience** ✅ Tasks 5-7 implement the chat / web subcommands with the documented ports, banners, and flags.
- **Spec § 5.1 directory layout** ✅ Tasks 1, 2, 4 produce the documented layout. Tests live in the existing `tests/Aexon.Commands.Tests/`.
- **Spec § 5.2 vite multi-entry** ✅ Task 4 step 6 implements it. The "post-build relocate script" risk called out in § 11 is addressed by Task 4 step 5.
- **Spec § 5.3 Workbench module wiring** ✅ Task 4 steps 3-4 implement the bridge shim + entry HTML.
- **Spec § 5.4 Host changes** ✅ Task 5 implements the signature change exactly as specified.
- **Spec § 5.5 Command changes** ✅ Task 6 implements the two methods, dispatch case, and PrintUsage update.
- **Spec § 5.6 Build pipeline** ✅ Task 9 wires `scripts/reinstall.sh`. csproj already has the wwwroot rule (no change needed). Task 1 step 3 adds .gitignore entries.
- **Spec § 6 trim plan** ✅ Tasks 2 + 3 cover all the deletions and edits in the documented order.
- **Spec § 7 Workbench port** ✅ Task 4 covers the file copies + verbatim drop + skipping the standalone HTML.
- **Spec § 9 verification plan** ✅ Task 10 covers all 8 verification points (build, host startup, chat surface, workbench surface, concurrent run, dotnet pack, install, existing tests).

No placeholders found. Type/method signatures consistent across tasks (e.g., `ParseWebFlags(string, int)` is the same in Task 6 and Task 7).

One area to flag for the executor: **Task 2 step 7 ("iterate tsc until clean")** is open-ended. It's an honest reflection of the trim — TypeScript will surface follow-on deletions you can't enumerate up front. Budget 30-60 minutes for that step alone.

---
