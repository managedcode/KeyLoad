import { Config, Css, Dom, Id, Sample } from './constants.js';
import { Text } from './text.js';
import { clearChart, donut, sparkline, timeChart } from './charts.js';
import { el, make, meter, write } from './dom.js';
import { bytes, clock, count, ms, percent, rate } from './format.js';
import { latest, pick, times } from './metrics.js';
import { failureClass, failureCode } from './errors.js';

const storageParts = storage => {
    const known = (storage.canonicalBytes ?? Config.zero) + (storage.replicaBytes ?? Config.zero) + (storage.backupBytes ?? Config.zero);
    const other = storage.totalBytes === null || storage.totalBytes === undefined ? null : Math.max(Config.zero, storage.totalBytes - known);
    return [
        { value: storage.canonicalBytes ?? Config.zero, cls: Css.c1 },
        { value: storage.replicaBytes ?? Config.zero, cls: Css.c2 },
        { value: storage.backupBytes ?? Config.zero, cls: Css.c3 },
        { value: other ?? Config.zero, cls: Css.c4, raw: other }
    ];
};

function kpis(snapshot) {
    const last = latest();
    write(Id.throughput, last ? rate(last.rate) : Text.dash);
    write(Id.latency, last ? ms(last.latency) : Text.dash);
    write(Id.errorRate, last ? percent(last.errorPct) : Text.dash);
    write(Id.errorRateNote, !last ? Text.twoSamples : last.completed
        ? `${count(last.failed)}${Text.failedOf}${count(last.completed)}${Text.requestsWord}` : Text.noRequests);
    write(Id.canonical, bytes(snapshot.storage.canonicalBytes));
    write(Id.canonicalNote, `${bytes(snapshot.storage.totalBytes)}${Text.totalOnNode}`);
    sparkline(Id.sparkThroughput, pick(Sample.rate));
    sparkline(Id.sparkLatency, pick(Sample.latency));
    sparkline(Id.sparkErrors, pick(Sample.errorPct));
    sparkline(Id.sparkStorage, pick(Sample.canonical));
}

function activity() {
    const stamps = times();
    el(Id.chartEmpty).hidden = stamps.length > Config.zero;
    if (!stamps.length) {
        clearChart(Id.activityChart);
        return;
    }
    timeChart(Id.activityChart, {
        times: stamps,
        stacked: true,
        format: (value, axis) => axis ? `${Number(value.toFixed(Config.digits))}` : rate(value),
        series: [
            { label: Text.succeeded, cls: Css.c1, values: pick(Sample.okRate) },
            { label: Text.failedSeries, cls: Css.bad, values: pick(Sample.failRate) }
        ]
    });
}

function storage(snapshot) {
    const value = snapshot.storage;
    const parts = storageParts(value);
    donut(Id.storageDonut, parts);
    write(Id.storageTotal, bytes(value.totalBytes));
    write(Id.storageCanonical, bytes(value.canonicalBytes));
    write(Id.storageReplica, bytes(value.replicaBytes));
    write(Id.storageBackup, bytes(value.backupBytes));
    write(Id.storageOther, bytes(parts[parts.length - Config.one].raw));
    const state = el(Id.storageState);
    state.className = value.complete ? Css.badgeGood : Css.badgeWarn;
    state.textContent = value.complete ? Text.completeShort : Text.partialShort;
    write(Id.storageNotice, `${value.complete ? Text.complete : Text.partial}${Text.dot}${value.observedFiles}${Text.files}${value.notice ? Text.dot + value.notice : Text.empty}`);
}

function admission(snapshot) {
    const usage = snapshot.admission.usage;
    const limits = snapshot.admission.limits;
    write(Id.admission, `${usage.commands} / ${limits.maxCommands}`);
    el(Id.admissionMeters).replaceChildren(
        meter(Text.commands, usage.commands, limits.maxCommands, `${usage.commands}${Text.slash}${limits.maxCommands}`),
        meter(Text.retainedBytes, usage.retainedBytes, limits.maxRetainedBytes, `${bytes(usage.retainedBytes)}${Text.slash}${bytes(limits.maxRetainedBytes)}`));
}

function recentErrors(snapshot) {
    const failures = snapshot.http.recentFailures ?? [];
    write(Id.recentErrorCount, count(failures.length));
    const list = el(Id.recentErrors);
    list.replaceChildren();
    if (!failures.length) {
        list.append(make(Dom.li, Text.noFailures, Css.muted));
        return;
    }
    failures.slice(Config.zero, Config.recentErrors).forEach(failure => {
        const item = make(Dom.li, Text.empty);
        item.append(make(Dom.span, failureCode(failure), `${Css.statusCode} ${failureClass(failure)}`),
            make(Dom.span, `${failure.method} ${failure.route}`, Css.route), make(Dom.time, clock(failure.at)));
        list.append(item);
    });
}

export function renderOverview(snapshot) {
    kpis(snapshot);
    activity();
    storage(snapshot);
    admission(snapshot);
    recentErrors(snapshot);
}

export function clearOverview() {
    [Id.throughput, Id.latency, Id.errorRate, Id.canonical, Id.admission, Id.storageCanonical, Id.storageReplica,
        Id.storageBackup, Id.storageOther, Id.storageTotal].forEach(id => write(id, Text.dash));
    write(Id.errorRateNote, Text.twoSamples);
    write(Id.canonicalNote, Text.storageWaiting);
    write(Id.storageNotice, Text.storageWaiting);
    write(Id.recentErrorCount, count(Config.zero));
    el(Id.recentErrors).replaceChildren(make(Dom.li, Text.noFailures, Css.muted));
    el(Id.admissionMeters).replaceChildren();
    const state = el(Id.storageState);
    state.className = Css.badge;
    state.textContent = Text.waiting;
    [Id.sparkThroughput, Id.sparkLatency, Id.sparkErrors, Id.sparkStorage, Id.storageDonut].forEach(id => el(id).replaceChildren());
    clearChart(Id.activityChart);
    el(Id.chartEmpty).hidden = false;
}
