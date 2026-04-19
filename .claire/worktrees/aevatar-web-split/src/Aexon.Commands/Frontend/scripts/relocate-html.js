#!/usr/bin/env node
// After `vite build`, Rollup emits index.html and workbench.html at the
// root of outDir, and per-entry JS/CSS into aevatar-{chat,workbench}/
// subdirs (per vite.config.ts entryFileNames). This script:
//   1. moves index.html -> aevatar-chat/index.html
//   2. moves workbench.html -> aevatar-workbench/index.html
//   3. rewrites the asset URLs in each so they resolve under the subdir
//   4. moves any staging/ assets to their owning subdir

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
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

// Copy any stray top-level subdirs (e.g. aevatar-client/ produced by
// Rollup's shared-chunk naming) into both target subdirs, then fix the
// HTML modulepreload/script hrefs that still reference those paths.
const subdirs = ['aevatar-chat', 'aevatar-workbench'];
const topEntries = fs.readdirSync(wwwroot);
for (const entry of topEntries) {
  if (subdirs.includes(entry)) continue;          // already handled
  const entryPath = path.join(wwwroot, entry);
  if (!fs.statSync(entryPath).isDirectory()) continue;

  // It's an unexpected chunk dir — copy files into both target subdirs,
  // rewrite HTML references, then delete.
  const files = fs.readdirSync(entryPath);
  for (const f of files) {
    const src = path.join(entryPath, f);
    for (const dest of subdirs) {
      fs.copyFileSync(src, path.join(wwwroot, dest, f));
    }
    fs.unlinkSync(src);
  }
  fs.rmdirSync(entryPath);

  // Rewrite references in the two HTML files: /${entry}/<file> -> /<file>
  for (const dest of subdirs) {
    const htmlPath = path.join(wwwroot, dest, 'index.html');
    if (!fs.existsSync(htmlPath)) continue;
    let html = fs.readFileSync(htmlPath, 'utf8');
    html = html.replaceAll(`/${entry}/`, '/');
    fs.writeFileSync(htmlPath, html);
  }
  console.log(`relocate: ${entry}/ flushed into subdirs`);
}
