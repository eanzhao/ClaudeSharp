#!/usr/bin/env node
// After `vite build`, Rollup emits index.html and workbench.html at the
// root of outDir, and per-entry JS/CSS into aevatar-{chat,workbench}/
// subdirs (per vite.config.ts entryFileNames). This script:
//   1. moves index.html -> aevatar-chat/index.html
//   2. moves workbench.html -> aevatar-workbench/index.html
//   3. rewrites the asset URLs in each so they resolve under the subdir
//   4. moves any staging/ assets to their owning subdir
//   5. copies any stray shared-chunk dirs into both subdirs and rewrites paths

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

const targetSubdirs = ['aevatar-chat', 'aevatar-workbench'];

// Move any asset that landed in staging/ into both target subdirs and
// rewrite the /staging/<file> references in the HTML files.
const staging = path.join(wwwroot, 'staging');
if (fs.existsSync(staging)) {
  for (const f of fs.readdirSync(staging)) {
    const src = path.join(staging, f);
    for (const dest of targetSubdirs) {
      fs.copyFileSync(src, path.join(wwwroot, dest, f));
    }
    fs.unlinkSync(src);
  }
  fs.rmdirSync(staging);
  // Rewrite /staging/<file> -> /<file> in both HTML files
  for (const dest of targetSubdirs) {
    const htmlPath = path.join(wwwroot, dest, 'index.html');
    if (!fs.existsSync(htmlPath)) continue;
    let html = fs.readFileSync(htmlPath, 'utf8');
    html = html.replaceAll('/staging/', '/');
    fs.writeFileSync(htmlPath, html);
  }
  console.log('relocate: staging/ flushed');
}

// Copy any remaining top-level subdirs (e.g. aevatar-client/ produced by
// Rollup's shared-chunk naming when chunkFileNames uses [name]) into both
// target subdirs, rewrite the HTML hrefs, then delete the stray dir.
const topEntries = fs.readdirSync(wwwroot);
for (const entry of topEntries) {
  if (targetSubdirs.includes(entry)) continue;   // already handled
  const entryPath = path.join(wwwroot, entry);
  if (!fs.statSync(entryPath).isDirectory()) continue;

  // Unexpected chunk dir — copy files into both target subdirs,
  // rewrite HTML references, then delete.
  for (const f of fs.readdirSync(entryPath)) {
    const src = path.join(entryPath, f);
    for (const dest of targetSubdirs) {
      fs.copyFileSync(src, path.join(wwwroot, dest, f));
    }
    fs.unlinkSync(src);
  }
  fs.rmdirSync(entryPath);

  // Rewrite /${entry}/<file> -> /<file> in both HTML files
  for (const dest of targetSubdirs) {
    const htmlPath = path.join(wwwroot, dest, 'index.html');
    if (!fs.existsSync(htmlPath)) continue;
    let html = fs.readFileSync(htmlPath, 'utf8');
    html = html.replaceAll(`/${entry}/`, '/');
    fs.writeFileSync(htmlPath, html);
  }
  console.log(`relocate: ${entry}/ flushed into subdirs`);
}
