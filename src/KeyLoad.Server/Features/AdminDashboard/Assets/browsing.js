import {
    Api,
    Config,
    Dom,
    Headers,
    Id,
    Kind,
    Text,
    View
} from './constants.js';
import {
    bytes,
    date,
    detailButton,
    el,
    make,
    resetTable,
    status,
    table,
    write
} from './dom.js';
import {
    physicalFiles
} from './rendering.js';
let resource = null;
let resourceAfter = null;
let dataAfter = null;
let activeView = View.overview;
let call = null;
let cancel = null;
const kindName = value => typeof value === typeof Config.zero?Kind.names[value]:value;
export function initializeBrowsing(request, cancelPending) {
    call = request;
    cancel = cancelPending;
}
export function hasSelectedResource() {
    return resource !== null;
}
export function viewChanged(view) {
    activeView = view;
    resetBrowsing();
    if (view === View.files)physicalFiles();
}
export function resetBrowsing() {
    resource = null;
    resourceAfter = null;
    dataAfter = null;
    el(Id.resourceList).replaceChildren(make(Dom.p, Text.loadResources));
    write(Id.resourceCount, Text.dash);
    el(Id.resourcesFirst).disabled = true;
    el(Id.resourcesNext).disabled = true;
    resetTable();
}
export function scope() {
    return {
        tenantId:el(Id.tenant).value.trim(),
        databaseId:el(Id.database).value.trim()
    };
}
function partition() {
    return {
        ...scope(),
        transactionDomainId:resource.transactionDomainId,
        partitionKey:el(Id.partition).value.trim()
    };
}
function expectedKind() {
    if (activeView === View.queues)return Kind.queue;
    if (activeView === View.files)return Kind.blob;
    return Kind.collection;
}
function validScope() {
    const value = scope();
    if (!value.tenantId || !value.databaseId || !el(Id.partition).value.trim()) {
        status(Text.scopeRequired, true);
        return false;
    }
    return true;
}
export async function loadResources(next = false) {
    if (!validScope())return;
    cancel();
    const afterName = next?resourceAfter:null;
    const result = await call(Api.resources, {
        ...scope(),
        afterName,
        limit:Config.pageSize
    });
    if (!result)return;
    resourceAfter = result.nextAfterName;
    resource = null;
    dataAfter = null;
    resetTable();
    if (activeView === View.files)physicalFiles();
    const items = result.items.filter(item => kindName(item.kind) === expectedKind());
    renderResources(items);
    el(Id.resourcesNext).disabled = !resourceAfter;
    el(Id.resourcesFirst).disabled = afterName === null;
}
function renderResources(items) {
    const list = el(Id.resourceList);
    list.replaceChildren();
    write(Id.resourceCount,`${items.length}${Text.resourceSuffix}`);
    if (!items.length) {
        const empty = make(Dom.p, Text.noResources);
        empty.className = Dom.emptyClass;
        list.append(empty);
        return;
    }
    items.forEach(item => {
        const button = make(Dom.button, item.name);
        button.type = Dom.buttonType;
        button.append(make(Dom.small,`${kindName(item.kind)}${Text.dot}${item.transactionDomainId}`));
        button.append(make(Dom.small, `${Text.schema}${item.schemaVersion}${Text.dot}${item.indexCount}${Text.indexes}${item.paused?Text.dot+Text.paused:Text.empty}`));
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
    if (!resource || !validScope())return;
    cancel();
    const selected = resource;
    const kind = kindName(selected.kind);
    const cursor = next?dataAfter:null;
    const result = await fetchData(kind, cursor);
    if (!result)return;
    write(Id.dataTitle, selected.name);
    if (kind === Kind.collection)documents(result);
    else if (kind === Kind.queue)queue(result);
    else if (kind === Kind.blob)blobs(result);
    else status(Text.unsupported, true);
    el(Id.dataNext).disabled = !dataAfter;
    el(Id.dataFirst).disabled = cursor === null;
}
function fetchData(kind, cursor) {
    const scopePartition = partition();
    if (kind === Kind.collection)return call(Api.query, {
        partition:scopePartition,
        query:{
            collection:resource.name,
            alias:null,
            projection:[],
            filter:null,
            order:[],
            limit:Config.rows,
            explain:false
        },
        parameters:null,
        allowFullScan:true,
        cursor,
        astVersion:Config.astVersion
    });
    if (kind === Kind.queue)return call(Api.queue, {
        lane:{
            partition:scopePartition,
            queue:resource.name
        },
        afterId:cursor,
        limit:Config.pageSize
    });
    if (kind === Kind.blob)return call(Api.blobs, {
        partition:scopePartition,
        resource:resource.name,
        limit:Config.pageSize,
        afterId:cursor
    });
    return null;
}
function documents(page) {
    dataAfter = page.cursor;
    write(Id.dataDescription, Text.documentHint);
    table(Headers.documents, page.rows.map(row => [row.entityId, row.revision, row.json.slice(Config.zero, Config.preview), detailButton(row.json)]));
    write(Id.dataEmpty, Text.noRows);
    write(Id.pageDescription,`${Text.cut}${Text.space}${page.cutPosition}`);
}
function queue(page) {
    dataAfter = page.nextAfterId;
    write(Id.dataDescription,`${page.counters.storedMessages}${Text.space}${Text.stored}${Text.dot}${page.counters.inFlightMessages}${Text.space}${Text.inFlight}${Text.dot}${Text.queueHint}`);
    table(Headers.queues, page.items.map(row => [row.id, row.state, row.attempts, row.stateVersion, date(row.notBefore), date(row.leaseUntil), date(row.expiresAt)]));
    write(Id.dataEmpty, Text.noRows);
    write(Id.pageDescription,`${Text.cut}${Text.space}${page.cutPosition}`);
}
function blobs(page) {
    dataAfter = page.nextAfterId;
    write(Id.dataDescription, Text.blobHint);
    table(Headers.blobs, page.items.map(row => [row.blob.id, bytes(row.length), row.revision, row.partCount, date(row.updatedAt)]));
    write(Id.dataEmpty, Text.noRows);
    write(Id.pageDescription, Text.page);
}
