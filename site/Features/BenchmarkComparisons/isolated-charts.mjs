import { ENGINE_TYPES } from './isolated-contracts.mjs';

const INTEGER = new Intl.NumberFormat('en', { maximumFractionDigits: 0 });
const ONE = new Intl.NumberFormat('en', { maximumFractionDigits: 1 });
const TWO = new Intl.NumberFormat('en', { maximumFractionDigits: 2 });
const SIGNED = new Intl.NumberFormat('en', { maximumFractionDigits: 0, signDisplay: 'exceptZero' });
const LATENCIES = Object.freeze(['p50', 'p95', 'p99']);
const HEAT_STEPS = 5;

export function el(document, tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}

// Chart labels round for reading; the table view keeps the recorded precision.
export function friendly(value, unit) {
  if (!Number.isFinite(value)) return '—';
  if (unit === 'ms') return value >= 1000 ? ONE.format(value / 1000) + ' s'
    : (value >= 10 ? INTEGER : value >= 1 ? ONE : TWO).format(value) + ' ms';
  if (unit === 's') return (value >= 100 ? INTEGER : value >= 10 ? ONE : TWO).format(value) + ' s';
  if (unit === '%') return TWO.format(value) + '%';
  return (value >= 100 ? INTEGER : ONE).format(value) + (unit ? ' ' + unit : '');
}

export function compact(value, unit) {
  if (!Number.isFinite(value) || unit !== 'ops/s') return friendly(value, unit);
  return value >= 1e6 ? ONE.format(value / 1e6) + 'M' : value >= 1e4 ? ONE.format(value / 1e3) + 'k' : INTEGER.format(value);
}

const position = (node, name, percent) => node.style.setProperty(name, Math.min(100, Math.max(0, percent)).toFixed(2) + '%');

export function engineName(document, name, tag = 'span') {
  const node = el(document, tag, 'engine-name');
  node.append(el(document, 'strong', null, name), el(document, 'small', null, ENGINE_TYPES[name]));
  return node;
}

export function latencyScale(rows) {
  const values = rows.flatMap(row => [row.p50, row.p99]).filter(value => Number.isFinite(value) && value > 0);
  if (values.length === 0) return null;
  const low = Math.floor(Math.log10(Math.min(...values)));
  const span = Math.max(1, Math.ceil(Math.log10(Math.max(...values))) - low);
  return { ticks: Array.from({ length: span + 1 }, (_, index) => 10 ** (low + index)),
    at: value => (Math.log10(Math.max(value, 10 ** low)) - low) / span * 100 };
}

function latency(document, row, scale) {
  const cell = el(document, 'span', 'rank-lat');
  const track = el(document, 'span', 'lat-track');
  const range = el(document, 'span', 'lat-range');
  position(range, '--from', scale.at(row.p50));
  position(range, '--to', scale.at(row.p99));
  track.append(range);
  for (const key of LATENCIES) {
    const dot = el(document, 'span', 'lat-dot lat-' + key);
    position(dot, '--at', scale.at(row[key]));
    dot.title = `${row.name} · ${key} ${friendly(row[key], 'ms')}`;
    track.append(dot);
  }
  cell.append(track, el(document, 'small', null, `p50 ${friendly(row.p50, 'ms')} · p99 ${friendly(row.p99, 'ms')}`));
  return cell;
}

export function rankChart(document, rows, metric) {
  const scale = latencyScale(rows);
  const chart = el(document, 'div', 'rank' + (scale ? ' has-latency' : ''));
  const head = el(document, 'div', 'rank-head');
  head.setAttribute('aria-hidden', 'true');
  head.append(el(document, 'span'), el(document, 'span', null, 'Engine'), el(document, 'span', null, `${metric.title} · ${metric.unit}`));
  if (scale) {
    const ticks = el(document, 'span', 'lat-ticks', 'Latency per request · log scale');
    for (const tick of scale.ticks) position(ticks.appendChild(el(document, 'i', null, friendly(tick, 'ms'))), '--at', scale.at(tick));
    head.append(ticks);
  }
  const list = el(document, 'ol', 'rank-list');
  list.setAttribute('aria-label', metric.title + ' ranking');
  const maximum = Math.max(0, ...rows.map(row => row.value));
  rows.forEach((row, index) => {
    const item = el(document, 'li', 'rank-row' + (row.name === 'KeyLoad' ? ' is-keyload' : ''));
    item.dataset.workerId = row.id;
    item.dataset.value = String(row.value);
    const bar = el(document, 'span', 'rank-bar');
    bar.style.setProperty('--ratio', String(maximum > 0 ? row.value / maximum : 0));
    const [number, unit] = metric.unit === 'ops/s' ? [friendly(row.value), metric.unit] : [friendly(row.value, metric.unit), ''];
    bar.append(el(document, 'span', 'rank-fill'), el(document, 'b', null, number));
    if (unit) bar.lastChild.append(el(document, 'small', null, ' ' + unit));
    item.append(el(document, 'span', 'rank-no', String(index + 1)), engineName(document, row.name), bar);
    if (scale) item.append(latency(document, row, scale));
    list.append(item);
  });
  chart.append(head, list);
  return chart;
}

function heat(cell, value, column) {
  if (!Number.isFinite(value)) {
    cell.className = 'heat-none';
    return;
  }
  const score = column.higher ? value / column.best : column.best / value;
  cell.className = 'heat-' + Math.min(HEAT_STEPS - 1, Math.floor(score * HEAT_STEPS)) + (score === 1 ? ' is-best' : '');
}

export function matrix(document, model) {
  const table = el(document, 'table', 'matrix');
  table.append(el(document, 'caption', null, model.caption));
  const header = el(document, 'tr');
  header.append(el(document, 'th', null, 'Engine'));
  for (const column of model.columns) {
    const th = el(document, 'th', column.value === model.selected ? 'is-selected' : null);
    th.scope = 'col';
    const button = el(document, 'button', null, column.label);
    button.type = 'button';
    button.dataset.scenario = column.value;
    button.append(el(document, 'small', null, column.group ?? column.unit));
    th.append(button);
    header.append(th);
  }
  table.appendChild(el(document, 'thead')).append(header);
  for (const group of model.groups) {
    const body = table.appendChild(el(document, 'tbody'));
    const label = body.appendChild(el(document, 'tr', 'matrix-group')).appendChild(el(document, 'th', null, group.type));
    label.colSpan = model.columns.length + 1;
    label.scope = 'rowgroup';
    for (const row of group.rows) {
      const tr = body.appendChild(el(document, 'tr', row.name === 'KeyLoad' ? 'is-keyload' : null));
      tr.append(el(document, 'th', null, row.name));
      tr.firstChild.scope = 'row';
      model.columns.forEach((column, index) => {
        const value = row.values[index];
        const td = tr.appendChild(el(document, 'td', null, compact(value, column.unit)));
        heat(td, value, column);
        if (Number.isFinite(value)) td.title = `${row.name} · ${column.title} · ${friendly(value, column.unit)}`;
      });
    }
  }
  return table;
}

export function scaling(document, pairs, metric) {
  const list = el(document, 'ul', 'scale-list');
  const maximum = Math.max(0, ...pairs.flatMap(pair => [pair.one, pair.three]));
  for (const pair of pairs) {
    const item = el(document, 'li', 'scale-row');
    const track = el(document, 'span', 'scale-track');
    const range = track.appendChild(el(document, 'span', 'scale-range'));
    const [one, three] = [pair.one, pair.three].map(value => maximum > 0 ? value / maximum * 100 : 0);
    position(range, '--from', Math.min(one, three));
    position(range, '--to', Math.max(one, three));
    for (const [className, at, value, nodes] of [['dot-one', one, pair.one, 1], ['dot-three', three, pair.three, 3]]) {
      const dot = track.appendChild(el(document, 'span', 'scale-dot ' + className));
      position(dot, '--at', at);
      dot.title = `${pair.name} · ${nodes} node${nodes === 1 ? '' : 's'} · ${friendly(value, metric.unit)}`;
    }
    const change = (pair.three - pair.one) / pair.one * 100;
    item.append(engineName(document, pair.name), track,
      el(document, 'span', 'scale-values', `${friendly(pair.one, metric.unit)} → ${friendly(pair.three, metric.unit)}`),
      el(document, 'b', 'scale-change', SIGNED.format(change).replace('-', '−') + '%'));
    list.append(item);
  }
  return list;
}
