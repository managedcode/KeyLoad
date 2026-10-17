import { Api, BrowseViews, Config, Css, Dom, Headers, Id, Kind, KindInfo, View } from './constants.js';
import { Text } from './text.js';
import { detailButton, el, make, resetTable, status, table, write } from './dom.js';
import { bytes, count, date } from './format.js';

let resource = null;
let resourceAfter = null;
let dataAfter = null;
let activeView = View.overview;
let pendingName = null;
let call = null;
let cancel = null;
const kindName = value => typeof value === typeof Config.zero ? Kind.names[value] : value;

export function initializeBrowsing(request, cancelPending) {
    call = request;
    cancel = cancelPending;
}

export function hasSelectedResource() {
    return resource !== null;
}

export function viewChanged(view, openName = null) {
    activeView = view;
    pendingName = openName;
    resetBrowsing();
}

export function resetBrowsing() {
    resource = null;
    resourceAfter = null;
    dataAfter = null;
    el(Id.resourceList).replaceChildren(make(Dom.p, Text.loadResources, Dom.emptyClass));
    write(Id.resourceCount, Text.dash);
    el(Id.resourcesFirst).disabled = true;
    el(Id.resourcesNext).disabled = true;
    resetTable();
}

export function scope() {
    return { tenantId: el(Id.tenant).value.trim(), databaseId: el(Id.database).value.trim() };
}

function partition() {
    return { ...scope(), transactionDomainId: resource.transactionDomainId, partitionKey: el(Id.partition).value.trim() };
}

function expectedKind() {
    if (activeView === View.queues) return Kind.queue;
    if (activeView === View.files) return Kind.blob;
    return Kind.collection;
}

export function validScope() {
    const value = scope();
    if (!value.tenantId || !value.databaseId || !el(Id.partition).value.trim()) {
        status(Text.scopeRequired, true);
        return false;
    }
    return true;
}

export function canBrowse() {
    if (!el(Id.disconnect).disabled) return true;
    status(Text.disconnectedHint);
    return false;
}

export async function loadResources(next = false) {
    if (!BrowseViews.includes(activeView)) return;
    const afterName = next ? resourceAfter : null;
    cancel();
    resetBrowsing();
    el(Id.resourceList).replaceChildren(make(Dom.p, Text.resourcesPending, Dom.emptyClass));
    if (!validScope() || !canBrowse()) return;
    status(Text.resourcesLoading);
    const result = await call(Api.resources, { ...scope(), afterName, limit: Config.pageSize });
    if (!result) return;
    resourceAfter = result.nextAfterName;
    renderResources(result.items.filter(item => kindName(item.kind) === expectedKind()));
    el(Id.resourcesNext).disabled = !resourceAfter;
    el(Id.resourcesFirst).disabled = afterName === null;
    status(Text.resourcesReady);
    openPending();
}

function openPending() {
    const name = pendingName;
    pendingName = null;
    if (name === null) return;
    const button = [...el(Id.resourceList).querySelectorAll(Dom.button)].find(node => node.dataset.name === name);
    button?.click();
}

function renderResources(items) {
    const list = el(Id.resourceList);
    list.replaceChildren();
    write(Id.resourceCount, `${items.length}${Text.resourceSuffix}`);
    if (!items.length) {
        list.append(make(Dom.p, Text.noResources, Dom.emptyClass));
        return;
    }
    items.forEach(item => {
        const button = make(Dom.button, item.name);
        button.type = Dom.buttonType;
        button.dataset.name = item.name;
        button.append(make(Dom.small, `${KindInfo[kindName(item.kind)]?.single ?? kindName(item.kind)}${Text.dot}${item.transactionDomainId}`));
        button.append(make(Dom.small, `${Text.schema}${item.schemaVersion}${Text.dot}${item.indexCount}${Text.indexes}${item.paused ? Text.dot + Text.paused : Text.empty}`));
        button.addEventListener(Dom.click, () => chooseResource(item, button));
        list.append(button);
    });
}

async function chooseResource(item, button) {
    resource = item;
    dataAfter = null;
    el(Id.resourceList).querySelectorAll(Dom.button).forEach(node => node.removeAttribute(Dom.pressed));
    button.setAttribute(Dom.pressed, Dom.true);
    await loadData();
}

export async function loadData(next = false) {
    const selected = resource;
    const cursor = next ? dataAfter : null;
    cancel();
    resetTable();
    dataAfter = null;
    if (!selected || !validScope() || !canBrowse()) return;
    const kind = kindName(selected.kind);
    write(Id.dataTitle, selected.name);
    write(Id.dataEmpty, Text.recordsPending);
    status(Text.recordsLoading);
    const result = await fetchData(kind, cursor);
    if (!result) return;
    if (kind === Kind.collection) documents(result);
    else if (kind === Kind.queue) queue(result);
    else if (kind === Kind.blob) blobs(result);
    else {
        status(Text.unsupported, true);
        return;
    }
    el(Id.dataNext).disabled = !dataAfter;
    el(Id.dataFirst).disabled = cursor === null;
    status(Text.recordsReady);
}

function fetchData(kind, cursor) {
    const scopePartition = partition();
    if (kind === Kind.collection) return call(Api.query, {
        partition: scopePartition,
        query: { collection: resource.name, alias: null, projection: [{ path: Config.wildcard, alias: Config.wildcard }], filter: null, order: [], limit: Config.rows, explain: false },
        parameters: null,
        allowFullScan: true,
        cursor,
        astVersion: Config.astVersion
    });
    if (kind === Kind.queue) return call(Api.queue, { lane: { partition: scopePartition, queue: resource.name }, afterId: cursor, limit: Config.pageSize });
    if (kind === Kind.blob) return call(Api.blobs, { partition: scopePartition, resource: resource.name, limit: Config.pageSize, afterId: cursor });
    return null;
}

function stateBadge(state) {
    const node = make(Dom.span, Text.empty, Css.badgeBrand);
    node.textContent = state;
    return node;
}

function documents(page) {
    dataAfter = page.cursor;
    write(Id.dataDescription, Text.documentHint);
    table(Headers.documents, page.rows.map(row => [row.entityId, row.revision, row.json.slice(Config.zero, Config.preview), detailButton(row.json)]), [Css.mono, Css.num, Css.preview]);
    write(Id.dataEmpty, Text.noRows);
    write(Id.pageDescription, `${Text.cut}${Text.space}${page.cutPosition}`);
}

function queue(page) {
    dataAfter = page.nextAfterId;
    write(Id.dataDescription, `${count(page.counters.storedMessages)}${Text.space}${Text.stored}${Text.dot}${count(page.counters.inFlightMessages)}${Text.space}${Text.inFlight}${Text.dot}${Text.queueHint}`);
    table(Headers.queues, page.items.map(row => [row.id, stateBadge(row.state), row.attempts, row.stateVersion, date(row.notBefore), date(row.leaseUntil), date(row.expiresAt)]), [Css.mono, Css.empty, Css.num, Css.num]);
    write(Id.dataEmpty, Text.noRows);
    write(Id.pageDescription, `${Text.cut}${Text.space}${page.cutPosition}`);
}

function blobs(page) {
    dataAfter = page.nextAfterId;
    write(Id.dataDescription, Text.blobHint);
    table(Headers.blobs, page.items.map(row => [row.blob.id, bytes(row.length), row.revision, row.partCount, date(row.updatedAt)]), [Css.mono, Css.num, Css.num, Css.num]);
    write(Id.dataEmpty, Text.noRows);
    write(Id.pageDescription, Text.page);
}
