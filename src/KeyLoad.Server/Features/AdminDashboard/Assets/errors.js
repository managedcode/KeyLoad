import { Config, Css, Dom, Headers, Id } from './constants.js';
import { Text } from './text.js';
import { el, fillTable, make, stat, write } from './dom.js';
import { clock, count, date, ms, percent } from './format.js';
import { latest } from './metrics.js';

const Filter = Object.freeze({ all: 'all', client: 'client', server: 'server', aborted: 'aborted' });
const filterLabels = [[Filter.all, Text.all], [Filter.client, Text.clientErrors], [Filter.server, Text.serverErrors], [Filter.aborted, Text.aborted]];
let filter = Filter.all;
let failures = [];
let events = [];
let seenAt = null;
let viewing = false;

export function failureClass(failure) {
    if (failure.aborted) return Css.aborted;
    return failure.statusCode >= Config.serverError ? Css.server : Css.client;
}

export const failureCode = failure => failure.aborted ? Text.abortedCode : String(failure.statusCode);

function matches(failure) {
    return filter === Filter.all || failureClass(failure) === filter;
}

function chips() {
    const host = el(Id.errorFilters);
    host.replaceChildren();
    filterLabels.forEach(([key, label]) => {
        const chip = make(Dom.button, label, Css.chip);
        chip.type = Dom.buttonType;
        chip.setAttribute(Dom.pressed, String(key === filter));
        chip.append(make(Dom.small, count(key === Filter.all ? failures.length : failures.filter(item => failureClass(item) === key).length)));
        chip.addEventListener(Dom.click, () => {
            filter = key;
            renderLog();
        });
        host.append(chip);
    });
}

function renderLog() {
    chips();
    const rows = failures.filter(matches).map(failure => [
        clock(failure.at),
        make(Dom.span, failureCode(failure), `${Css.statusCode} ${failureClass(failure)}`),
        failure.method,
        failure.route,
        ms(failure.elapsedMilliseconds)
    ]);
    fillTable(Id.errorTable, Id.errorEmpty, rows.length ? Headers.errors : [], rows, [Css.num, Css.empty, Css.mono, Css.mono, Css.num]);
    write(Id.errorEmpty, failures.length ? Text.noFilterMatch : Text.noFailuresNode);
}

function stats(snapshot) {
    const last = latest();
    const newest = failures[Config.zero];
    el(Id.errorStats).replaceChildren(
        stat(Text.lifetimeFailures, count(snapshot.http.failedRequests), Text.sinceStart),
        stat(Text.errorRate, last ? percent(last.errorPct) : Text.dash, last && !last.completed ? Text.noRequests : Text.thisSession),
        stat(Text.lastFailure, newest ? clock(newest.at) : Text.never, newest ? date(newest.at) : null),
        stat(Text.retained, count(failures.length), Text.retainedHint));
}

function badge() {
    const unseen = seenAt === null ? failures.length : failures.filter(item => Date.parse(item.at) > seenAt).length;
    const node = el(Id.navErrorCount);
    node.hidden = viewing || unseen === Config.zero;
    node.textContent = count(unseen);
}

function markSeen() {
    if (failures.length) seenAt = Date.parse(failures[Config.zero].at);
}

export function renderErrors(snapshot) {
    failures = snapshot.http.recentFailures ?? [];
    if (viewing) markSeen();
    stats(snapshot);
    renderLog();
    badge();
}

export function errorsVisible(visible) {
    viewing = visible;
    if (visible) markSeen();
    badge();
}

function renderEvents() {
    const list = el(Id.eventLog);
    list.replaceChildren();
    if (!events.length) {
        list.append(make(Dom.li, Text.noEvents, Css.muted));
        return;
    }
    events.forEach(entry => {
        const item = make(Dom.li, Text.empty);
        item.append(make(Dom.span, entry.label, entry.cls), make(Dom.span, entry.text, Css.textClass), make(Dom.time, clock(entry.at)));
        list.append(item);
    });
}

export function logEvent(level, text) {
    const cls = level === Css.bad ? Css.badgeBad : level === Css.warn ? Css.badgeWarn : Css.badgeGood;
    const label = level === Css.bad ? Text.failedSeries : level === Css.warn ? Text.warning : Text.info;
    events.unshift({ at: new Date().toISOString(), cls, label, text });
    if (events.length > Config.maxEvents) events.pop();
    renderEvents();
}

export function clearErrors() {
    failures = [];
    events = [];
    seenAt = null;
    filter = Filter.all;
    el(Id.errorStats).replaceChildren();
    renderLog();
    renderEvents();
    badge();
}
