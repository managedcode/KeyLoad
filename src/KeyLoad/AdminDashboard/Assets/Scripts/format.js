import { Config } from './constants.js';
import { Text } from './text.js';

const missing = value => value === null || value === undefined || Number.isNaN(value);
const fixed = value => value.toLocaleString(undefined, { maximumFractionDigits: Config.digits });

export function bytes(value) {
    if (missing(value)) return Text.dash;
    let unit = Config.zero;
    let size = value;
    while (size >= Config.kilo && unit < Text.bytes.length - Config.one) {
        size /= Config.kilo;
        unit += Config.one;
    }
    return `${fixed(size)}${Text.space}${Text.bytes[unit]}`;
}

export function compact(value) {
    if (missing(value)) return Text.dash;
    let unit = Config.zero;
    let size = value;
    while (Math.abs(size) >= Config.thousand && unit < Text.compact.length - Config.one) {
        size /= Config.thousand;
        unit += Config.one;
    }
    return `${fixed(size)}${Text.compact[unit]}`;
}

export const rate = value => missing(value) ? Text.dash : `${fixed(value)}${Text.perSecond}`;
export const ms = value => missing(value) ? Text.dash : `${fixed(value)}${Text.ms}`;
export const percent = value => missing(value) ? Text.dash : `${fixed(value)}${Text.pct}`;
export const count = value => missing(value) ? Text.dash : value.toLocaleString();

export function date(value) {
    return value ? new Date(value).toLocaleString() : Text.dash;
}

export function clock(value) {
    return value ? new Date(value).toLocaleTimeString() : Text.dash;
}

export function duration(milliseconds) {
    if (missing(milliseconds) || milliseconds < Config.zero) return Text.dash;
    const minutes = Math.floor(milliseconds / Config.millis / Config.secondsPerMinute);
    const hours = Math.floor(minutes / Config.minutesPerHour);
    const days = Math.floor(hours / Config.hoursPerDay);
    if (days > Config.zero) return `${days}${Text.day}${hours % Config.hoursPerDay}${Text.hour.trim()}`;
    if (hours > Config.zero) return `${hours}${Text.hour}${minutes % Config.minutesPerHour}${Text.minute}`;
    if (minutes > Config.zero) return `${minutes}${Text.minute}`;
    return `${Math.floor(milliseconds / Config.millis)}${Text.second}`;
}

export function shortId(value) {
    if (!value) return Text.dash;
    if (URL.canParse(value)) return new URL(value).hostname;
    return value.length > Config.longId ? value.slice(Config.zero, Config.shortId) : value;
}
