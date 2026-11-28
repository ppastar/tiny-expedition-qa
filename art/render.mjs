// 전투 캐릭터 SVG(art/units/*.svg)·무대 소품 SVG(art/props/*.svg) → 부위별 PNG + manifest.json
//   node render.mjs           PNG와 manifest를 다시 만든다
//   node render.mjs --check   SVG가 바뀌었는데 PNG를 다시 만들지 않았으면 실패 (manifest의 해시와 비교)
//
// SVG 규칙
//   - 맨 위 <svg>에 viewBox, data-ppu(1배 크기에서 유닛당 픽셀). 발밑 가운데가 기준점이다.
//   - 부위는 맨 위 <g id="L-이름">: back(머리와 같이 움직이는 뒷머리), wings(날갯짓), body, head.
//   - 눈은 <g id="eyes-open"> / <g id="eyes-closed" display="none"> 한 쌍. 감은 눈으로 한 장 더 뽑아 깜빡임에 쓴다.
//   - data-neck="x y": 머리가 까딱이는 축, data-flap="x y": 날개가 접히는 축 (SVG 좌표).
import { Resvg } from '@resvg/resvg-js';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const srcDirs = [join(here, 'units'), join(here, 'props')];
const outDir = join(here, '..', 'UnityProject', 'Assets', 'StreamingAssets', 'BattleArt');
const skinDir = join(here, '..', 'UnityProject', 'Assets', 'StreamingAssets', 'Skins');
const manifestPath = join(outDir, 'manifest.json');
const SCALE = 2;
const LAYERS = ['back', 'wings', 'body', 'head'];

const sha = text => createHash('sha256').update(text.replace(/\r\n/g, '\n')).digest('hex').slice(0, 16);
const attr = (svg, name) => (svg.match(new RegExp(`<svg[^>]*[ ]${name}="([^"]*)"`)) || [])[1];

function describe(path) {
  const svg = readFileSync(path, 'utf8');
  const file = basename(path);
  const key = file.replace(/\.svg$/, '');
  const [, , w, h] = attr(svg, 'viewBox').split(/\s+/).map(Number);
  const ppu = Number(attr(svg, 'data-ppu'));
  const layers = LAYERS.filter(l => svg.includes(`<g id="L-${l}"`));
  if (!ppu || !layers.includes('body')) throw new Error(`${file}: data-ppu와 L-body가 필요합니다`);
  // SVG 좌표(왼쪽 위 기준, 아래로 +) → 유닛(발밑 가운데 기준, 위로 +)
  const point = name => {
    const v = attr(svg, name);
    if (!v) return null;
    const [x, y] = v.split(/\s+/).map(Number);
    return { x: +((x - w / 2) / ppu).toFixed(4), y: +((h - y) / ppu).toFixed(4) };
  };
  const eyesIn = layers.find(l => layerBody(svg, l).includes('id="eyes-open"')) || '';
  return { key, svg, w, h, entry: { key, hash: sha(svg), ppu: ppu * SCALE, layers, blink: eyesIn, neck: point('data-neck') || { x: 0, y: 0 }, flap: point('data-flap') || { x: 0, y: 0 } } };
}

function layerBody(svg, layer) {
  const start = svg.indexOf(`<g id="L-${layer}"`);
  const next = LAYERS.map(l => svg.indexOf(`<g id="L-${l}"`, start + 1)).filter(i => i > start).sort((a, b) => a - b)[0];
  return svg.slice(start, next || svg.length);
}

function png(svg, width, show, closedEyes) {
  let s = svg;
  for (const l of LAYERS) if (!show.includes(l)) s = s.replace(`<g id="L-${l}"`, `<g id="L-${l}" display="none"`);
  if (closedEyes) s = s.replace('<g id="eyes-open"', '<g id="eyes-open" display="none"').replace('<g id="eyes-closed" display="none"', '<g id="eyes-closed"');
  return new Resvg(s, { fitTo: { mode: 'width', value: width }, font: { loadSystemFonts: false } }).render().asPng();
}

// ---- 모집 카드 ----
const CARD_W = 280, CARD_H = 400;
const RARITY = { ThreeStar: ['#ffe9a8', '#ffc857', '#e89a2e'], TwoStar: ['#cfe8ff', '#6cb8ff', '#3f86d8'], OneStar: ['#d9dee8', '#8a93a8', '#667089'] };
const characters = JSON.parse(readFileSync(join(here, '..', 'UnityProject', 'Assets', 'StreamingAssets', 'GameData', 'characters.json'), 'utf8'));
const rarityOf = Object.fromEntries(characters.map(c => [c.Id, c.Rarity]));

function cardSvg(u) {
  const [light, base, deep] = RARITY[rarityOf[u.key]] || RARITY.OneStar;
  const open = u.svg.indexOf('<svg'), openEnd = u.svg.indexOf('>', open), close = u.svg.lastIndexOf('</svg>');
  const rootAttrs = u.svg.slice(open + 4, openEnd);
  const inner = u.svg.slice(openEnd + 1, close);
  const w = 300, h = Math.round(w * u.h / u.w);
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${CARD_W} ${CARD_H}">
  <defs>
    <linearGradient id="card-bg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${light}"/><stop offset=".6" stop-color="${base}"/><stop offset="1" stop-color="${deep}"/></linearGradient>
    <linearGradient id="card-fade" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1b1e2a" stop-opacity="0"/><stop offset="1" stop-color="#1b1e2a" stop-opacity=".88"/></linearGradient>
    <clipPath id="card-clip"><rect width="${CARD_W}" height="${CARD_H}" rx="20"/></clipPath>
  </defs>
  <g clip-path="url(#card-clip)">
    <rect width="${CARD_W}" height="${CARD_H}" fill="url(#card-bg)"/>
    <circle cx="140" cy="150" r="128" fill="#fff" opacity=".28"/>
    <circle cx="140" cy="150" r="96" fill="#fff" opacity=".22"/>
    <path d="M-20 330 L120 -20 H170 L30 330Z M150 420 L300 60 V150 L200 420Z" fill="#fff" opacity=".1"/>
    <svg x="${(CARD_W - w) / 2}" y="8" width="${w}" height="${h}"${rootAttrs}>${inner}</svg>
    <rect y="220" width="${CARD_W}" height="${CARD_H - 220}" fill="url(#card-fade)"/>
  </g>
  <rect x="3" y="3" width="${CARD_W - 6}" height="${CARD_H - 6}" rx="17" fill="none" stroke="#fff" stroke-opacity=".7" stroke-width="5"/>
</svg>`;
}

// 소품(p_*)은 캐릭터와 같은 규칙(L-body 필수, L-head는 따로 움직일 부분)을 따르고 모집 카드는 만들지 않는다
const files = srcDirs.filter(existsSync).flatMap(d => readdirSync(d).filter(f => f.endsWith('.svg')).map(f => join(d, f)))
  .sort((a, b) => basename(a).localeCompare(basename(b)));
const units = files.map(describe);
const manifest = { version: 1, units: units.map(u => u.entry) };

if (process.argv.includes('--check')) {
  const old = existsSync(manifestPath) ? JSON.parse(readFileSync(manifestPath, 'utf8')) : { units: [] };
  const problems = [];
  for (const u of manifest.units) {
    const o = old.units.find(x => x.key === u.key);
    if (!o) problems.push(`${u.key}: manifest에 없음`);
    else if (o.hash !== u.hash) problems.push(`${u.key}: SVG가 바뀌었는데 PNG를 다시 만들지 않음`);
    for (const f of [...u.layers, 'full', ...(u.blink ? ['blink'] : [])])
      if (!existsSync(join(outDir, `${u.key}.${f}.png`))) problems.push(`${u.key}.${f}.png 없음`);
  }
  for (const o of old.units) if (!manifest.units.some(u => u.key === o.key)) problems.push(`${o.key}: SVG 원본이 없음`);
  if (problems.length) { console.error(problems.join('\n')); process.exit(1); }
  console.log(`그림 ${manifest.units.length}개: SVG와 PNG가 맞습니다`);
  process.exit(0);
}

mkdirSync(outDir, { recursive: true });
mkdirSync(skinDir, { recursive: true });
for (const u of units) {
  const width = u.w * SCALE;
  for (const l of u.entry.layers) writeFileSync(join(outDir, `${u.key}.${l}.png`), png(u.svg, width, [l], false));
  if (u.entry.blink) writeFileSync(join(outDir, `${u.key}.blink.png`), png(u.svg, width, [u.entry.blink], true));
  writeFileSync(join(outDir, `${u.key}.full.png`), png(u.svg, width, LAYERS, false));
  // 모집 결과 카드(StreamingAssets/Skins/<캐릭터 id>.png): 등급 색 배경 위에 같은 그림
  if (rarityOf[u.key]) writeFileSync(join(skinDir, `${u.key}.png`), new Resvg(cardSvg(u), { fitTo: { mode: 'width', value: CARD_W * SCALE }, font: { loadSystemFonts: false } }).render().asPng());
  console.log(`${u.key}: ${u.entry.layers.join(', ')}${u.entry.blink ? ' + blink' : ''}`);
}
writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
