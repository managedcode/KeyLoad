import { Api, Config, Css, Dom, Headers, Id, Kind, KindInfo } from './constants.js';
import { Text } from './text.js';
import { el, fillTable, make, status, swatch, write } from './dom.js';
import { count } from './format.js';
import { canBrowse, scope, validScope } from './browsing.js';

const allKinds = null;
let call = null;
let cancel = null;
let open = null;
let items = [];
let after = null;
let paged = false;
let filter = allKinds;

const kindName = value => typeof value === typeof Config.zero ? Kind.names[value] : value;

export function initializeCatalog(request, cancelPending, openResource) {
    call = request;
    cancel = cancelPending;
    open = openResource;
    el(Id.catalogFirst).addEventListener(Dom.click, () => loadCatalog());
    el(Id.catalogNext).addEventListener(Dom.click, () => loadCatalog(true));
}

function chip(label, value, total) {
    const button = make(Dom.button, label, Css.chip);
    button.type = Dom.buttonType;
    button.setAttribute(Dom.pressed, String(filter === value));
    if (value !== allKinds) button.prepend(swatch(KindInfo[value].cls));
    button.append(make(Dom.small, count(total)));
    button.addEventListener(Dom.click, () => {
        filter = value;
        renderCatalog();
    });
    return button;
}

function chips() {
    const host = el(Id.catalogChips);
    host.replaceChildren(chip(Text.all, allKinds, items.length));
    Kind.names.forEach(name => {
        const total = items.filter(item => kindName(item.kind) === name).length;
        if (total) host.append(chip(KindInfo[name].label, name, total));
    });
}

function browseCell(item) {
    const info = KindInfo[kindName(item.kind)];
    if (!info?.view) return Text.notBrowsable;
    const button = make(Dom.button, Text.open);
    button.type = Dom.buttonType;
    button.addEventListener(Dom.click, () => open(info.view, item.name));
    return button;
}

function kindCell(item) {
    const info = KindInfo[kindName(item.kind)];
    const node = make(Dom.span, Text.empty, `${Css.kind} ${info?.cls ?? Css.muted}`);
    node.append(swatch(Css.empty), document.createTextNode(info?.single ?? String(item.kind)));
    return node;
}

function renderCatalog() {
    chips();
    const rows = items.filter(item => filter === allKinds || kindName(item.kind) === filter).map(item => [
        item.name, kindCell(item), item.transactionDomainId, `${Text.schemaShort}${item.schemaVersion}`,
        count(item.indexCount), make(Dom.span, item.paused ? Text.pausedState : Text.active, item.paused ? Css.badgeWarn : Css.badgeGood),
        browseCell(item)
    ]);
    fillTable(Id.catalogTable, Id.catalogEmpty, rows.length ? Headers.catalog : [], rows, [Css.mono, Css.empty, Css.mono, Css.num, Css.num]);
    write(Id.catalogEmpty, Text.noResources);
}

export async function loadCatalog(next = false) {
    const afterName = next ? after : null;
    cancel();
    resetCatalog();
    write(Id.catalogEmpty, Text.resourcesPending);
    if (!validScope() || !canBrowse()) return;
    status(Text.resourcesLoading);
    const result = await call(Api.resources, { ...scope(), afterName, limit: Config.pageSize });
    if (!result) return;
    items = result.items;
    after = result.nextAfterName;
    paged = afterName !== null;
    renderCatalog();
    el(Id.catalogNext).disabled = !after;
    el(Id.catalogFirst).disabled = !paged;
    write(Id.catalogPage, `${Text.cut}${Text.space}${result.cutPosition}`);
    status(Text.resourcesReady);
}

export function resetCatalog() {
    items = [];
    filter = allKinds;
    el(Id.catalogChips).replaceChildren();
    fillTable(Id.catalogTable, Id.catalogEmpty, [], []);
    write(Id.catalogEmpty, Text.loadResources);
    write(Id.catalogPage, Text.page);
    el(Id.catalogNext).disabled = true;
    el(Id.catalogFirst).disabled = true;
}

export function clearCatalog() {
    after = null;
    paged = false;
    resetCatalog();
}
