import { Category, Config, Css, Dom, Headers, Id } from './constants.js';
import { Text } from './text.js';
import { el, fillTable, make, swatch, write } from './dom.js';
import { bytes, count } from './format.js';

const categoryOf = name => Category[name] ?? Category.other;

function totals(storage) {
    const known = (storage.canonicalBytes ?? Config.zero) + (storage.replicaBytes ?? Config.zero) + (storage.backupBytes ?? Config.zero);
    return [
        [Category.canonical, storage.canonicalBytes],
        [Category.replica, storage.replicaBytes],
        [Category.backup, storage.backupBytes],
        [Category.other, storage.totalBytes === null || storage.totalBytes === undefined ? null : Math.max(Config.zero, storage.totalBytes - known)]
    ];
}

function bar(storage) {
    const host = el(Id.diskBar);
    const legend = el(Id.diskLegend);
    host.replaceChildren();
    legend.replaceChildren();
    const parts = totals(storage);
    const total = parts.reduce((sum, [, value]) => sum + (value ?? Config.zero), Config.zero);
    parts.forEach(([category, value]) => {
        if (total > Config.zero && value > Config.zero) {
            const segment = swatch(category.cls);
            segment.style.width = `${value / total * Config.hundred}${Dom.percent}`;
            host.append(segment);
        }
        const item = make(Dom.span, Text.empty);
        item.append(swatch(category.cls), document.createTextNode(`${category.label}${Text.dot}${bytes(value)}`));
        legend.append(item);
    });
}

function sizeCell(file, largest, category) {
    const wrap = make(Dom.div, Text.empty, Css.sizeCell);
    const track = make(Dom.span, Text.empty, `${Css.sizeBar} ${category.cls}`);
    const fill = swatch(Css.empty);
    fill.style.width = `${largest > Config.zero ? file.bytes / largest * Config.hundred : Config.zero}${Dom.percent}`;
    track.append(fill);
    wrap.append(make(Dom.span, bytes(file.bytes), Css.num), track);
    return wrap;
}

function files(storage) {
    const sorted = [...(storage.files ?? [])].sort((left, right) => right.bytes - left.bytes);
    const largest = sorted.length ? sorted[Config.zero].bytes : Config.zero;
    const rows = sorted.map(file => {
        const category = categoryOf(file.category);
        const kind = make(Dom.span, Text.empty, Css.kind);
        kind.append(swatch(category.cls), document.createTextNode(category.label));
        return [file.path, kind, sizeCell(file, largest, category)];
    });
    fillTable(Id.diskTable, Id.diskEmpty, rows.length ? Headers.physical : [], rows, [Css.mono, Css.empty, Css.empty]);
    write(Id.diskEmpty, Text.noRows);
}

export function renderStorage(snapshot) {
    const storage = snapshot.storage;
    write(Id.diskTotal, bytes(storage.totalBytes));
    write(Id.diskFiles, `${count(storage.observedFiles)}${Text.files}${Text.dot}${count(storage.files?.length ?? Config.zero)}${Text.filesShown}${storage.notice ? Text.dot + storage.notice : Text.empty}`);
    const state = el(Id.diskState);
    state.className = storage.complete ? Css.badgeGood : Css.badgeWarn;
    state.textContent = storage.complete ? Text.complete : Text.partial;
    bar(storage);
    files(storage);
}

export function clearStorage() {
    write(Id.diskTotal, Text.dash);
    write(Id.diskFiles, Text.storageWaiting);
    const state = el(Id.diskState);
    state.className = Css.badge;
    state.textContent = Text.waiting;
    el(Id.diskBar).replaceChildren();
    el(Id.diskLegend).replaceChildren();
    fillTable(Id.diskTable, Id.diskEmpty, [], []);
    write(Id.diskEmpty, Text.storageWaiting);
}
