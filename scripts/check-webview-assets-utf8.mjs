/**
 * Fail if Field Trip / Surface map WebView assets are UTF-16 or contain null bytes.
 * Usage: node scripts/check-webview-assets-utf8.mjs
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const DIRS = [
  join(ROOT, 'src', 'CaveAiProForWindows', 'Assets', 'field-trip-map'),
  join(ROOT, 'src', 'CaveAiProForWindows', 'Assets', 'surface-map'),
];
const EXT = new Set(['.html', '.js', '.css']);

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    const st = statSync(p);
    if (st.isDirectory()) walk(p, out);
    else if (EXT.has(name.slice(name.lastIndexOf('.')).toLowerCase())) out.push(p);
  }
  return out;
}

let failed = false;
for (const dir of DIRS) {
  for (const file of walk(dir)) {
    const buf = readFileSync(file);
    const rel = relative(ROOT, file);
    if (buf.length >= 2 && buf[0] === 0xff && buf[1] === 0xfe) {
      console.error('UTF-16 LE BOM:', rel);
      failed = true;
      continue;
    }
    if (buf.length >= 3 && buf[0] === 0xef && buf[1] === 0xbb && buf[2] === 0xbf) {
      // UTF-8 BOM ok but warn
      console.warn('UTF-8 BOM (prefer no BOM):', rel);
    }
    let nulls = 0;
    for (let i = 0; i < Math.min(buf.length, 4096); i++) if (buf[i] === 0) nulls++;
    if (nulls > 8) {
      console.error('Suspicious null bytes:', rel, 'count=', nulls);
      failed = true;
    }
  }
}

if (failed) {
  console.error('check-webview-assets-utf8: FAILED');
  process.exit(1);
}
console.log('check-webview-assets-utf8: OK');