import React, { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import ReactDOM from 'react-dom/client';

// The design's JSX files (atoms.jsx, chrome.jsx, ...) were authored for a
// `<script>`-tag world (React UMD + Babel-standalone) where every file
// shares one global scope and cross-file references work via `window.X`.
//
// ES modules can't reproduce that: each file is isolated, so a bare
// `React.createElement(...)` or `useState(...)` reference throws
// `ReferenceError`. We can't just put React on `window` and expect bare
// identifiers to fall back to globals (strict mode forbids it).
//
// Workaround: expose React + the hooks the design uses on `window`, then
// inject each design script as a non-module `<script>` tag in dependency
// order. Non-module scripts run in global scope, so window.React etc. are
// reachable as bare identifiers and `window.X = X` cross-file wiring works
// exactly as the design assumed.
//
// The design files live under `Frontend/public/aevatar-workbench-bridge/`
// so Vite ships them verbatim (not bundled as modules).

window.React = React;
window.ReactDOM = ReactDOM;
window.useState = useState;
window.useEffect = useEffect;
window.useMemo = useMemo;
window.useRef = useRef;
window.useCallback = useCallback;

const cssLink = document.createElement('link');
cssLink.rel = 'stylesheet';
cssLink.href = '/aevatar-workbench-bridge/styles/tokens.css';
document.head.appendChild(cssLink);

const designScripts = [
  // demo-data first — populates window.DEMO via IIFE
  'demo-data.js',
  // primitive atoms (Icon, Prov, KindBadge, ...) — others depend on these
  'atoms.js',
  // shell components (TopBar, ServiceList, ServiceHeader)
  'chrome.js',
  // step screens
  'build.js',
  'bind.js',
  'invoke.js',
  'observe.js',
  // tweaks panel
  'tweaks.js',
  // app.js calls ReactDOM.createRoot(...).render(...) — must be last
  'app.js',
];

(async () => {
  for (const name of designScripts) {
    await new Promise((resolve, reject) => {
      const s = document.createElement('script');
      s.src = `/aevatar-workbench-bridge/js/${name}`;
      s.onload = resolve;
      s.onerror = (err) => reject(new Error(`failed to load ${s.src}`));
      document.head.appendChild(s);
    });
  }
})().catch((err) => {
  console.error('[workbench] design script load failed:', err);
  const root = document.getElementById('root');
  if (root) {
    root.textContent = `Workbench failed to load: ${err.message}`;
  }
});
