import { Config, Css, Id, Sample } from './constants.js';
import { Text } from './text.js';
import { clearChart, timeChart } from './charts.js';
import { el, meter, stat } from './dom.js';
import { bytes, count, duration, ms, percent, rate } from './format.js';
import { latest, peak, pick, sessionLatency, times } from './metrics.js';

const axisNumber = value => `${Number(value.toFixed(Config.digits))}`;

function stats(snapshot) {
    const http = snapshot.http;
    const lifetimeLatency = http.completedRequests > Config.zero ? http.elapsedMilliseconds / http.completedRequests : null;
    const uptime = Date.parse(snapshot.capturedAt) - Date.parse(http.startedAt);
    el(Id.perfStats).replaceChildren(
        stat(Text.current, rate(latest()?.rate), Text.thisSession),
        stat(Text.sessionPeak, rate(peak(Sample.rate)), Text.thisSession),
        stat(Text.sessionLatency, ms(sessionLatency()), Text.thisSession),
        stat(Text.lifetimeRequests, count(http.completedRequests), Text.sinceStart),
        stat(Text.lifetimeLatency, ms(lifetimeLatency), Text.sinceStart),
        stat(Text.uptime, duration(uptime), Text.sinceStart));
}

function charts() {
    const stamps = times();
    if (!stamps.length) {
        [Id.perfThroughput, Id.perfLatency, Id.perfErrors].forEach(clearChart);
        return;
    }
    timeChart(Id.perfThroughput, {
        times: stamps, stacked: true, format: (value, axis) => axis ? axisNumber(value) : rate(value),
        series: [{ label: Text.succeeded, cls: Css.c1, values: pick(Sample.okRate) }, { label: Text.failedSeries, cls: Css.bad, values: pick(Sample.failRate) }]
    });
    timeChart(Id.perfLatency, {
        times: stamps, stacked: false, format: (value, axis) => axis ? axisNumber(value) : ms(value),
        series: [{ label: Text.latency, cls: Css.c7, values: pick(Sample.latency) }]
    });
    timeChart(Id.perfErrors, {
        times: stamps, stacked: false, format: (value, axis) => axis ? axisNumber(value) : percent(value),
        series: [{ label: Text.errorRate, cls: Css.bad, values: pick(Sample.errorPct) }]
    });
}

function admission(snapshot) {
    const usage = snapshot.admission.usage;
    const limits = snapshot.admission.limits;
    const pair = (value, limit, format) => `${format(value)}${Text.slash}${format(limit)}`;
    el(Id.perfAdmission).replaceChildren(
        meter(Text.commands, usage.commands, limits.maxCommands, pair(usage.commands, limits.maxCommands, count)),
        meter(Text.retainedBytes, usage.retainedBytes, limits.maxRetainedBytes, pair(usage.retainedBytes, limits.maxRetainedBytes, bytes)),
        meter(Text.controlCommands, usage.controlCommands, limits.reservedControlCommands, pair(usage.controlCommands, limits.reservedControlCommands, count)),
        meter(Text.tenantScopes, usage.activeTenantScopes, limits.maxCommands, count(usage.activeTenantScopes)),
        meter(Text.principalScopes, usage.activePrincipalScopes, limits.maxCommands, count(usage.activePrincipalScopes)));
}

export function renderPerformance(snapshot) {
    stats(snapshot);
    charts();
    admission(snapshot);
}

export function clearPerformance() {
    el(Id.perfStats).replaceChildren();
    el(Id.perfAdmission).replaceChildren();
    [Id.perfThroughput, Id.perfLatency, Id.perfErrors].forEach(clearChart);
}
