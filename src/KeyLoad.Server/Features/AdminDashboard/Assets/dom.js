import { Config, Css, Dom, Id } from './constants.js';
import { Text } from './text.js';

export const el = id => document.getElementById(id);

export function write(id, value) {
    el(id).textContent = value ?? Text.dash;
}

export function make(tag, value, className) {
    const node = document.createElement(tag);
    node.textContent = value ?? Text.dash;
    if (className) node.className = className;
    return node;
}

export function svg(tag, attributes = {}, parent = null) {
    const node = document.createElementNS(Css.svgNamespace, tag);
    Object.entries(attributes).forEach(([name, value]) => node.setAttribute(name, value));
    parent?.append(node);
    return node;
}

export function swatch(cls) {
    const mark = make(Dom.i, Text.empty, cls);
    mark.setAttribute(Css.ariaHidden, Dom.true);
    return mark;
}

export function status(message, error = false) {
    write(Id.status, message);
    el(Id.status).toggleAttribute(Dom.error, error);
}

export function cell(value, className) {
    const node = make(Dom.td, Text.empty, className);
    if (value instanceof Node) node.append(value);
    else node.textContent = value ?? Text.dash;
    return node;
}

export function fillTable(tableId, emptyId, headers, rows, classes = []) {
    const tableNode = el(tableId);
    tableNode.tHead.replaceChildren();
    tableNode.tBodies[Config.zero].replaceChildren();
    if (headers.length) {
        const heading = make(Dom.tr, Text.empty);
        headers.forEach(value => heading.append(make(Dom.th, value)));
        tableNode.tHead.append(heading);
    }
    rows.forEach(values => {
        const row = make(Dom.tr, Text.empty);
        values.forEach((value, index) => row.append(cell(value, classes[index])));
        tableNode.tBodies[Config.zero].append(row);
    });
    el(emptyId).hidden = rows.length > Config.zero;
}

export function table(headers, rows, classes) {
    fillTable(Id.dataTable, Id.dataEmpty, headers, rows, classes);
}

export function details(value) {
    const pre = el(Id.dialog).querySelector(Dom.pre);
    pre.textContent = JSON.stringify(value, null, Config.jsonIndent);
    el(Id.dialog).showModal();
}

export function detailButton(json) {
    const button = make(Dom.button, Text.details);
    button.type = Dom.buttonType;
    button.addEventListener(Dom.click, () => {
        try {
            details(JSON.parse(json));
        } catch {
            details(json);
        }
    });
    return button;
}

export function resetTable() {
    el(Id.dialog).close();
    el(Id.dialog).querySelector(Dom.pre).textContent = Text.empty;
    table([], []);
    write(Id.dataTitle, Text.select);
    write(Id.dataDescription, Text.browseHint);
    write(Id.dataEmpty, Text.selectHint);
    write(Id.pageDescription, Text.page);
    el(Id.dataFirst).disabled = true;
    el(Id.dataNext).disabled = true;
}

export function meter(label, value, limit, display) {
    const ratio = limit > Config.zero ? Math.min(Config.one, value / limit) : Config.zero;
    const wrap = make(Dom.div, Text.empty);
    const head = make(Dom.div, Text.empty, Css.meterLabel);
    head.append(make(Dom.span, label), make(Dom.b, display));
    const track = make(Dom.div, Text.empty, Css.meterTrack);
    const fill = make(Dom.div, Text.empty, Css.meterFill);
    if (ratio >= Config.meterBad) fill.classList.add(Css.bad);
    else if (ratio >= Config.meterWarn) fill.classList.add(Css.warn);
    fill.style.width = `${ratio * Config.hundred}${Dom.percent}`;
    track.append(fill);
    wrap.append(head, track);
    return wrap;
}

export function stat(label, value, note) {
    const node = make(Dom.div, Text.empty, Css.stat);
    node.append(make(Dom.span, label), make(Dom.strong, value));
    if (note) node.append(make(Dom.small, note));
    return node;
}
