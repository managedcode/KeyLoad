import {
    Config,
    Dom,
    Id,
    Text
} from './constants.js';
export const el  =  id => document.getElementById(id);
export function write(id, value) {
    el(id).textContent = value??Text.dash;
}
export function make(tag, value) {
    const node = document.createElement(tag);
    node.textContent = value??Text.dash;
    return node;
}
export function status(message, error = false) {
    write(Id.status, message);
    el(Id.status).toggleAttribute(Dom.error, error);
}
export function bytes(value) {
    if (value === null || value === undefined)return Text.dash;
    let unit = Config.zero;
    let size = value;
    while (size >= Config.kilo && unit<Text.bytes.length-Config.one) {
        size/= Config.kilo;
        unit +=  Config.one;
    }
    return `${size.toLocaleString(undefined,{maximumFractionDigits:Config.digits})}${Text.space}${Text.bytes[unit]}`;
}
export function date(value) {
    return value?new Date(value).toLocaleString():Text.dash;
}
export function table(headers, rows) {
    const tableNode = el(Id.dataTable);
    tableNode.tHead.replaceChildren();
    tableNode.tBodies[Config.zero].replaceChildren();
    const heading = make(Dom.tr, Text.empty);
    headers.forEach(value => heading.append(make(Dom.th, value)));
    tableNode.tHead.append(heading);
    rows.forEach(values => {
        const row = make(Dom.tr, Text.empty);
        values.forEach(value => {
            const cell = make(Dom.td, Text.empty);
            if (value instanceof Node)cell.append(value);
            else cell.textContent = value??Text.dash;
            row.append(cell);
        });
        tableNode.tBodies[Config.zero].append(row);
    });
    el(Id.dataEmpty).hidden = rows.length>Config.zero;
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
        }
        catch{
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
