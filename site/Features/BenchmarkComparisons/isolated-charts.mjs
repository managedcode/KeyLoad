import { ENGINE_TYPES } from './isolated-contracts.mjs';

const INTEGER = new Intl.NumberFormat('en', { maximumFractionDigits: 0 });
const ONE = new Intl.NumberFormat('en', { maximumFractionDigits: 1 });
const TWO = new Intl.NumberFormat('en', { maximumFractionDigits: 2 });
const SIGNED = new Intl.NumberFormat('en', { maximumFractionDigits: 0, signDisplay: 'exceptZero' });
export const NODES = Object.freeze([1, 3]);

export function el(document, tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}

// Chart labels round for reading; the results table keeps each exact recorded value in its cell title.
export function friendly(value, unit) {
  if (unit === 'ms') return value >= 1000 ? ONE.format(value / 1000) + ' s'
    : (value >= 10 ? INTEGER : value >= 1 ? ONE : TWO).format(value) + ' ms';
  if (unit === 's') return (value >= 100 ? INTEGER : value >= 10 ? ONE : TWO).format(value) + ' s';
  return (value >= 100 ? INTEGER : ONE).format(value);
}

export const compact = value => value >= 1e6 ? ONE.format(value / 1e6) + 'M'
  : value >= 1e4 ? ONE.format(value / 1e3) + 'k' : INTEGER.format(value);
export const nodesLabel = count => count + (count === 1 ? ' node' : ' nodes');
export const change = (one, three) => SIGNED.format((three - one) / one * 100).replace('-', '−') + '%';
const percent = ratio => (Math.min(1, Math.max(0, ratio)) * 100).toFixed(2) + '%';

export function engineName(document, name, tag = 'span') {
  const node = el(document, tag, 'engine-name');
  node.append(el(document, 'strong', null, name), el(document, 'small', null, ENGINE_TYPES[name]));
  return node;
}

// Two stacked bars, one per topology; a missing value states "No data" and its recorded reason.
export function pair(document, items) {
  const cell = el(document, 'span', 'pair');
  for (const item of items) {
    const line = cell.appendChild(el(document, 'span', 'pair-line n' + item.nodes));
    line.title = `${nodesLabel(item.nodes)} · ${item.title}`;
    line.dataset.value = item.value === null ? '' : String(item.value);
    if (item.value === null) {
      line.append(el(document, 'small', 'no-data', 'No data · ' + item.reason));
      continue;
    }
    line.appendChild(el(document, 'i')).style.setProperty('--size', percent(item.ratio));
    line.append(el(document, 'b', null, item.text));
    if (item.note) line.append(el(document, 'small', null, item.note));
  }
  return cell;
}

export function logScale(values) {
  const finite = values.filter(value => Number.isFinite(value) && value > 0);
  const low = Math.floor(Math.log10(Math.min(...finite)));
  const span = Math.max(1, Math.ceil(Math.log10(Math.max(...finite))) - low);
  return { ticks: Array.from({ length: span + 1 }, (_, index) => 10 ** (low + index)),
    at: value => percent((Math.log10(value) - low) / span) };
}

export function ticks(document, scale, label) {
  const row = el(document, 'span', 'lat-ticks', label);
  for (const tick of scale.ticks) row.appendChild(el(document, 'i', null, friendly(tick, 'ms'))).style.setProperty('--at', scale.at(tick));
  return row;
}

// p50 to p99 per topology on one shared log axis; p95 is the hollow mark between them.
export function latency(document, entries, scale) {
  const track = el(document, 'span', 'lat-track');
  for (const entry of entries) {
    if (entry.p50 === null) continue;
    const range = track.appendChild(el(document, 'span', 'lat-range n' + entry.nodes));
    range.style.setProperty('--from', scale.at(entry.p50));
    range.style.setProperty('--to', scale.at(entry.p99));
    track.appendChild(el(document, 'span', 'lat-mark n' + entry.nodes)).style.setProperty('--at', scale.at(entry.p95));
    range.title = `${nodesLabel(entry.nodes)} · p50 ${friendly(entry.p50, 'ms')} · p95 ${friendly(entry.p95, 'ms')} · p99 ${friendly(entry.p99, 'ms')}`;
  }
  return track;
}

// 3-node throughput as a share of the 1-node run on one panel-wide scale; the tick marks 1-node speed.
export function retained(document, one, three, scale) {
  const cell = el(document, 'span', 'keep');
  if (!(one > 0) || three === null) {
    cell.append(el(document, 'small', 'no-data', 'No data'));
    return cell;
  }
  cell.title = `3 nodes run at ${INTEGER.format(three / one * 100)}% of 1-node throughput`;
  cell.append(el(document, 'b', null, change(one, three)));
  const track = cell.appendChild(el(document, 'span', 'keep-track'));
  track.style.setProperty('--base', percent(1 / scale));
  track.appendChild(el(document, 'i')).style.setProperty('--size', percent(three / one / scale));
  return cell;
}
