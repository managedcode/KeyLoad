import { Config } from './constants.js';

let previous = null;
let samples = [];

export function resetMetrics() {
    previous = null;
    samples = [];
}

function comparable(next) {
    return previous && previous.node.nodeId === next.node.nodeId
        && previous.http.processInstance === next.http.processInstance
        && Date.parse(next.capturedAt) > Date.parse(previous.capturedAt)
        && next.http.completedRequests >= previous.http.completedRequests
        && next.http.failedRequests >= previous.http.failedRequests
        && next.http.elapsedMilliseconds >= previous.http.elapsedMilliseconds;
}

function interval(next) {
    const seconds = (Date.parse(next.capturedAt) - Date.parse(previous.capturedAt)) / Config.millis;
    const completed = next.http.completedRequests - previous.http.completedRequests;
    const failed = next.http.failedRequests - previous.http.failedRequests;
    const elapsed = next.http.elapsedMilliseconds - previous.http.elapsedMilliseconds;
    return {
        time: Date.parse(next.capturedAt),
        completed,
        failed,
        rate: completed / seconds,
        okRate: (completed - failed) / seconds,
        failRate: failed / seconds,
        latency: completed > Config.zero ? elapsed / completed : null,
        errorPct: completed > Config.zero ? failed / completed * Config.hundred : null,
        canonical: next.storage.canonicalBytes ?? null,
        total: next.storage.totalBytes ?? null
    };
}

/** Records one observation; returns true when rate history was reset because samples are not comparable. */
export function measure(next) {
    if (!comparable(next)) {
        const reset = previous !== null;
        samples = [];
        previous = next;
        return reset;
    }
    samples.push(interval(next));
    if (samples.length > Config.maxSamples) samples.shift();
    previous = next;
    return false;
}

export const history = () => samples;
export const latest = () => samples.length ? samples[samples.length - Config.one] : null;
export const pick = key => samples.map(sample => sample[key]);
export const times = () => samples.map(sample => sample.time);

export function peak(key) {
    const values = pick(key).filter(value => value !== null);
    return values.length ? Math.max(...values) : null;
}

export function sessionLatency() {
    const completed = samples.reduce((sum, sample) => sum + sample.completed, Config.zero);
    const weighted = samples.reduce((sum, sample) => sum + (sample.latency ?? Config.zero) * sample.completed, Config.zero);
    return completed > Config.zero ? weighted / completed : null;
}
