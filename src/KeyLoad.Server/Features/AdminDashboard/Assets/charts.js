import { Config, Css, Dom } from './constants.js';
import { el, svg } from './dom.js';
import { clock } from './format.js';
import { hideTooltip, showTooltip } from './tooltip.js';

const registry = new Map();
const observer = new ResizeObserver(entries => entries.forEach(entry => draw(entry.target.id)));
const niceSteps = Config.niceSteps;

function niceMax(value) {
    if (!(value > Config.zero)) return Config.one;
    const raw = value / Config.yTicks;
    const magnitude = 10 ** Math.floor(Math.log10(raw));
    const step = niceSteps.find(candidate => candidate * magnitude >= raw) * magnitude;
    return step * Config.yTicks;
}

function runs(values) {
    const result = [];
    let current = [];
    values.forEach((value, index) => {
        if (value === null || value === undefined) {
            if (current.length) result.push(current);
            current = [];
        } else current.push(index);
    });
    if (current.length) result.push(current);
    return result;
}

function linePath(indices, x, y) {
    return indices.map((index, order) => `${order ? Css.lineTo : Css.moveTo}${x(index)},${y(index)}`).join(Css.space);
}

function areaPath(indices, x, top, bottom) {
    const upper = indices.map((index, order) => `${order ? Css.lineTo : Css.moveTo}${x(index)},${top(index)}`);
    const lower = [...indices].reverse().map(index => `${Css.lineTo}${x(index)},${bottom(index)}`);
    return `${upper.join(Css.space)}${Css.space}${lower.join(Css.space)}${Css.close}`;
}

function stackTops(model) {
    let below = model.times.map(() => Config.zero);
    return model.series.map(series => {
        const base = below;
        const top = series.values.map((value, index) => value === null ? null : (model.stacked ? base[index] : Config.zero) + value);
        if (model.stacked) below = top.map((value, index) => value ?? base[index]);
        return { series, base: model.stacked ? base : model.times.map(() => Config.zero), top };
    });
}

function frame(host, model) {
    const width = host.clientWidth;
    const height = host.clientHeight;
    const left = Config.padLeft;
    const right = width - Config.padRight;
    const top = Config.padTop;
    const bottom = height - Config.padBottom;
    const first = model.times[Config.zero];
    const span = Math.max(Config.one, model.times[model.times.length - Config.one] - first);
    const layers = stackTops(model);
    const peak = Math.max(...layers.flatMap(layer => layer.top.filter(value => value !== null)), Config.zero);
    const max = niceMax(peak);
    return {
        width, height, left, right, top, bottom, max, layers,
        x: index => model.times.length === Config.one ? (left + right) / Config.two : left + (model.times[index] - first) / span * (right - left),
        y: value => bottom - value / max * (bottom - top)
    };
}

function stepDecimals(step) {
    let decimals = Config.zero;
    while (decimals < Config.maxAxisDecimals && Math.abs(step * 10 ** decimals - Math.round(step * 10 ** decimals)) > Config.epsilon)
        decimals += Config.one;
    return decimals;
}

function axes(root, model, geometry) {
    const group = svg(Css.g, { class: Css.axis }, root);
    const step = geometry.max / Config.yTicks;
    const decimals = stepDecimals(step);
    for (let tick = Config.zero; tick <= Config.yTicks; tick++) {
        const value = step * tick;
        const y = geometry.y(value);
        svg(Css.line, { x1: geometry.left, x2: geometry.right, y1: y, y2: y, class: tick ? Css.empty : Css.base }, group);
        svg(Css.text, { x: geometry.left - Config.labelGap, y: y + Config.dotRadius, [Css.anchor]: Css.end }, group).textContent = value.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    }
    const first = model.times[Config.zero];
    const span = model.times[model.times.length - Config.one] - first;
    const fit = Math.max(Config.one, Math.floor((geometry.right - geometry.left) / Config.xLabelWidth));
    const steps = span > Config.zero ? Math.min(Config.xTicks, fit) : Config.zero;
    for (let tick = Config.zero; tick <= steps; tick++) {
        const time = steps ? first + span / steps * tick : first;
        const x = steps ? geometry.left + (geometry.right - geometry.left) / steps * tick : geometry.x(Config.zero);
        const anchor = !steps ? Css.middle : tick === Config.zero ? Css.start : tick === steps ? Css.end : Css.middle;
        svg(Css.text, { x, y: geometry.height - Config.labelGap, [Css.anchor]: anchor }, group).textContent = clock(time);
    }
}

function series(root, geometry) {
    geometry.layers.forEach(layer => {
        const group = svg(Css.g, { class: `${Css.series} ${layer.series.cls}` }, root);
        runs(layer.top).forEach(indices => {
            const area = areaPath(indices, geometry.x, index => geometry.y(layer.top[index]), index => geometry.y(layer.base[index]));
            svg(Css.path, { d: area, class: Css.area }, group);
            svg(Css.path, { d: linePath(indices, geometry.x, index => geometry.y(layer.top[index])) }, group);
        });
    });
}

function hover(root, model, geometry) {
    const marks = svg(Css.g, {}, root);
    const hit = svg(Css.rect, { x: geometry.left, y: geometry.top, width: Math.max(Config.zero, geometry.right - geometry.left), height: Math.max(Config.zero, geometry.bottom - geometry.top), class: Css.hit }, root);
    hit.addEventListener(Dom.pointerMove, event => {
        const box = hit.getBoundingClientRect();
        const ratio = (event.clientX - box.left) / Math.max(Config.one, box.width);
        const target = geometry.left + ratio * (geometry.right - geometry.left);
        const index = model.times.reduce((best, _, candidate) => Math.abs(geometry.x(candidate) - target) < Math.abs(geometry.x(best) - target) ? candidate : best, Config.zero);
        marks.replaceChildren();
        svg(Css.line, { x1: geometry.x(index), x2: geometry.x(index), y1: geometry.top, y2: geometry.bottom, class: Css.crosshair }, marks);
        geometry.layers.forEach(layer => {
            if (layer.top[index] === null) return;
            svg(Css.circle, { cx: geometry.x(index), cy: geometry.y(layer.top[index]), r: Config.dotRadius, class: `${Css.hoverDot} ${layer.series.cls}` }, marks);
        });
        showTooltip(event, clock(model.times[index]), model.series.map(item => ({ label: item.label, cls: item.cls, value: model.format(item.values[index]) })));
    });
    hit.addEventListener(Dom.pointerLeave, () => {
        marks.replaceChildren();
        hideTooltip();
    });
}

function draw(hostId) {
    const host = el(hostId);
    const model = registry.get(hostId);
    if (!host || !model || !host.clientWidth || !model.times.length) return;
    const geometry = frame(host, model);
    const root = svg(Css.svg, { viewBox: `${Config.zero} ${Config.zero} ${geometry.width} ${geometry.height}`, [Css.ariaHidden]: Dom.true });
    axes(root, model, geometry);
    series(root, geometry);
    hover(root, model, geometry);
    host.replaceChildren(root);
}

export function timeChart(hostId, model) {
    if (!registry.has(hostId)) observer.observe(el(hostId));
    registry.set(hostId, model);
    draw(hostId);
}

export function clearChart(hostId) {
    registry.delete(hostId);
    el(hostId).replaceChildren();
}

export function sparkline(id, values) {
    const node = el(id);
    node.replaceChildren();
    const present = values.filter(value => value !== null && value !== undefined);
    if (present.length < Config.two) return;
    const max = Math.max(...present, Config.zero) || Config.one;
    const usable = Config.sparkHeight - Config.sparkPad * Config.two;
    const x = index => index / (values.length - Config.one) * Config.sparkWidth;
    const y = index => Config.sparkHeight - Config.sparkPad - values[index] / max * usable;
    runs(values).forEach(indices => {
        svg(Css.path, { d: areaPath(indices, x, y, () => Config.sparkHeight), class: Css.area, [Css.nonScaling]: Css.nonScalingValue }, node);
        svg(Css.path, { d: linePath(indices, x, y), [Css.nonScaling]: Css.nonScalingValue }, node);
    });
}

export function donut(id, parts) {
    const node = el(id);
    node.replaceChildren();
    const circumference = Config.two * Math.PI * Config.donutRadius;
    const base = { cx: Config.donutCenter, cy: Config.donutCenter, r: Config.donutRadius };
    svg(Css.circle, { ...base, class: Css.track }, node);
    const total = parts.reduce((sum, part) => sum + part.value, Config.zero);
    if (!(total > Config.zero)) return;
    const visible = parts.filter(part => part.value > Config.zero);
    const gap = visible.length > Config.one ? Config.donutGap : Config.zero;
    let offset = Config.zero;
    visible.forEach(part => {
        const length = Math.max(Config.half, part.value / total * circumference - gap);
        svg(Css.circle, { ...base, class: `${Css.segment} ${part.cls}`, [Css.dashArray]: `${length} ${circumference}`, [Css.dashOffset]: -offset }, node);
        offset += part.value / total * circumference;
    });
}
