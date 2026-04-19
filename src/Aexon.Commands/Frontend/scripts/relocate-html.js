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
// target subdirs, PRESERVING the directory structure. Don't rewrite paths:
// the chunk loader code baked into app.js references chunks via absolute
// URLs like '/aevatar-client/client-XXXX.js', so we mirror that path under
// each per-subcommand webroot. Copy + delete the stray top-level dir.
const topEntries = fs.readdirSync(wwwroot);
const otherDirs = topEntries.filter(
  (entry) =>
    !targetSubdirs.includes(entry) &&
    fs.statSync(path.join(wwwroot, entry)).isDirectory(),
);
if (otherDirs.length > 0) {
  console.warn(
    `relocate: WARNING — found ${otherDirs.length} stray top-level chunk dir(s): ` +
    `${otherDirs.join(', ')}. ` +
    `Mirroring path-preserving copy into BOTH entry subdirs so that the chunk ` +
    `loader's absolute URLs in app.js still resolve under each per-subcommand ` +
    `webroot. If these are per-entry chunks (not shared), refine vite.config.ts ` +
    `chunkFileNames to land them in the owning entry subdir.`
  );
}
for (const entry of topEntries) {
  if (targetSubdirs.includes(entry)) continue;   // already handled
  const entryPath = path.join(wwwroot, entry);
  if (!fs.statSync(entryPath).isDirectory()) continue;

  // Mirror entryPath/** into <subdir>/<entry>/** for each target subdir,
  // preserving the directory name so app.js's '/<entry>/<file>' URLs work.
  // Use cpSync for recursive copies (chunk dirs are usually flat, but
  // public/ assets can have nested subdirectories like js/, styles/).
  for (const dest of targetSubdirs) {
    const destDir = path.join(wwwroot, dest, entry);
    fs.cpSync(entryPath, destDir, { recursive: true });
  }
  fs.rmSync(entryPath, { recursive: true, force: true });
  console.log(`relocate: ${entry}/ mirrored into subdirs (path preserved)`);
}
