import { Api, BrowseViews, Config, Css, Dom, Id, View } from './constants.js';
import { Text } from './text.js';
import { el, status, write } from './dom.js';
import { initializeBrowsing, loadData, loadResources, resetBrowsing, viewChanged } from './browsing.js';
import { clearCatalog, initializeCatalog, loadCatalog } from './catalog.js';
import { clearErrors, errorsVisible, logEvent, renderErrors } from './errors.js';
import { date, shortId } from './format.js';
import { measure, resetMetrics } from './metrics.js';
import { initializeNavigation, selectView } from './navigation.js';
import { clearNodes, renderNodes } from './nodes.js';
import { clearOverview, renderOverview } from './overview.js';
import { clearPerformance, renderPerformance } from './performance.js';
import { clearStorage, renderStorage } from './storage.js';

let credential = Text.empty;
let epoch = Config.zero;
let controller = null;
let inFlight = false;
let poll = null;
let view = View.overview;
let observed = null;

function connection(active) {
    document.body.classList.toggle(Dom.connected, active);
    write(Id.state, active ? Text.connected : Text.disconnected);
    write(Id.liveText, active ? Text.live : Text.offline);
    el(Id.disconnect).disabled = !active;
    el(Id.refresh).disabled = !active;
}

function stopPolling() {
    if (poll !== null) clearTimeout(poll);
    poll = null;
}

function schedule() {
    stopPolling();
    if (credential && !document.hidden) poll = setTimeout(refresh, Config.pollMs);
}

function cancelPending() {
    epoch += Config.one;
    controller?.abort();
    controller = null;
    inFlight = false;
    el(Id.refresh).classList.remove(Dom.spinning);
    schedule();
}

function clearObservations() {
    observed = null;
    resetMetrics();
    clearOverview();
    clearPerformance();
    clearErrors();
    clearNodes();
    clearStorage();
    clearCatalog();
    write(Id.captured, Text.awaiting);
    el(Id.dialog).close();
    el(Id.dialog).querySelector(Dom.pre).textContent = Text.empty;
}

function disconnect(message = Text.disconnectedHint, resetScope = true) {
    cancelPending();
    credential = Text.empty;
    stopPolling();
    el(Id.key).value = Text.empty;
    if (resetScope) {
        el(Id.tenant).value = Text.defaultTenant;
        el(Id.database).value = Text.defaultDatabase;
        el(Id.partition).value = Text.defaultPartition;
    }
    connection(false);
    clearObservations();
    resetBrowsing();
    status(message, message === Text.unauthorized);
}

function observationFailed() {
    resetMetrics();
    clearOverview();
    clearPerformance();
    write(Id.state, Text.observationUnavailable);
    write(Id.liveText, Text.paused);
    write(Id.captured, Text.stale);
    el(Id.refresh).disabled = false;
    status(Text.failed, true);
    logEvent(Css.bad, Text.eventFailed);
}

async function send(path, body, signal) {
    const headers = { [Api.authorization]: Api.bearer + credential };
    if (body !== null) headers[Api.contentType] = Api.json;
    return fetch(path, {
        method: body === null ? Api.get : Api.post,
        headers,
        body: body === null ? undefined : JSON.stringify(body),
        credentials: Api.sameOrigin,
        cache: Api.noStore,
        signal
    });
}

async function request(path, body = null) {
    if (!credential || inFlight) return null;
    inFlight = true;
    const started = epoch;
    const current = new AbortController();
    controller = current;
    stopPolling();
    el(Id.refresh).classList.add(Dom.spinning);
    try {
        const response = await send(path, body, current.signal);
        if (started !== epoch) return null;
        if (response.status === Config.unauthenticated || response.status === Config.forbidden) {
            disconnect(Text.unauthorized);
            return null;
        }
        if (!response.ok) throw new Error(Text.httpError + response.status + Text.closeParen);
        const result = await response.json();
        return started === epoch ? result : null;
    } catch (error) {
        if (started !== epoch || error.name === Api.abort) return null;
        observationFailed();
        return null;
    } finally {
        if (started === epoch) {
            inFlight = false;
            controller = null;
            el(Id.refresh).classList.remove(Dom.spinning);
            schedule();
        }
    }
}

function noteTransitions(snapshot) {
    if (!observed) logEvent(Css.good, Text.eventConnected + shortId(snapshot.node.nodeId));
    if (!snapshot.node.routingReady && observed?.node.routingReady !== false) logEvent(Css.warn, Text.eventRouting);
    if (!snapshot.storage.complete && observed?.storage.complete !== false)
        logEvent(Css.warn, Text.eventPartial + (snapshot.storage.notice ?? Text.partial));
    observed = snapshot;
}

function render(snapshot) {
    if (measure(snapshot)) logEvent(Css.warn, Text.eventReset);
    noteTransitions(snapshot);
    renderOverview(snapshot);
    renderPerformance(snapshot);
    renderErrors(snapshot);
    renderNodes(snapshot);
    renderStorage(snapshot);
    write(Id.captured, Text.captured + date(snapshot.capturedAt));
}

/** Automatic polls pause in background tabs; explicit operator actions always refresh. */
async function refresh(manual = false) {
    if (!credential || inFlight || (!manual && document.hidden)) return;
    status(Text.loading);
    const result = await request(Api.snapshot);
    if (!result) return;
    connection(true);
    render(result);
    status(Text.ready);
}

async function connect(event) {
    event.preventDefault();
    const key = el(Id.key).value.trim();
    if (!key) return;
    disconnect(Text.disconnectedHint, false);
    credential = key;
    el(Id.key).value = Text.empty;
    write(Id.state, Text.connecting);
    el(Id.disconnect).disabled = false;
    await refresh(true);
}

async function navigate(next, openName = null) {
    cancelPending();
    view = next;
    selectView(view);
    viewChanged(view, openName);
    errorsVisible(view === View.errors);
    schedule();
    if (!credential) return;
    if (view === View.catalog) await loadCatalog();
    else if (BrowseViews.includes(view)) await loadResources();
}

function wireBrowsing() {
    el(Id.scope).addEventListener(Dom.submit, event => {
        event.preventDefault();
        if (view === View.catalog) loadCatalog();
        else loadResources();
    });
    el(Id.resourcesFirst).addEventListener(Dom.click, () => loadResources());
    el(Id.resourcesNext).addEventListener(Dom.click, () => loadResources(true));
    el(Id.dataFirst).addEventListener(Dom.click, () => loadData());
    el(Id.dataNext).addEventListener(Dom.click, () => loadData(true));
}

function start() {
    initializeBrowsing(request, cancelPending);
    initializeCatalog(request, cancelPending, navigate);
    initializeNavigation(navigate);
    clearObservations();
    el(Id.form).addEventListener(Dom.submit, connect);
    el(Id.disconnect).addEventListener(Dom.click, () => disconnect());
    el(Id.refresh).addEventListener(Dom.click, () => refresh(true));
    el(Id.closeDetails).addEventListener(Dom.click, () => el(Id.dialog).close());
    document.addEventListener(Dom.visibility, () => {
        if (document.hidden) stopPolling();
        else refresh();
    });
    wireBrowsing();
}

start();
