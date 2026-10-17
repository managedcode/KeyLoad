import { Config, Dom, Id } from './constants.js';
import { el, make, swatch } from './dom.js';

export function showTooltip(event, title, rows) {
    const tip = el(Id.tooltip);
    tip.replaceChildren(make(Dom.strong, title));
    rows.forEach(row => {
        const line = make(Dom.div, null);
        line.replaceChildren();
        const label = make(Dom.span, null);
        label.replaceChildren(swatch(row.cls), document.createTextNode(row.label));
        line.append(label, make(Dom.b, row.value));
        tip.append(line);
    });
    tip.hidden = false;
    const width = tip.offsetWidth;
    const height = tip.offsetHeight;
    let left = event.clientX + Config.tooltipGap;
    let top = event.clientY + Config.tooltipGap;
    if (left + width > innerWidth) left = event.clientX - width - Config.tooltipGap;
    if (top + height > innerHeight) top = event.clientY - height - Config.tooltipGap;
    tip.style.left = `${Math.max(Config.zero, left)}${Dom.px}`;
    tip.style.top = `${Math.max(Config.zero, top)}${Dom.px}`;
}

export function hideTooltip() {
    el(Id.tooltip).hidden = true;
}
