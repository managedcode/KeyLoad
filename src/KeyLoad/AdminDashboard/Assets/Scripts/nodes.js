import { ClusterLabels, Config, Css, Dom, Id } from './constants.js';
import { Text } from './text.js';
import { el, make, svg, write } from './dom.js';
import { bytes, date, duration, shortId } from './format.js';

const Layout = Object.freeze({
    full: { width: 560, cellHeight: 132, radius: 28, label: 48, sub: 67 },
    mini: { width: 240, cellHeight: 64, radius: 12, label: 28, sub: 42 }
});
const glyphPath = 'M-9 -7c0-2 4-3.5 9-3.5s9 1.5 9 3.5-4 3.5-9 3.5-9-1.5-9-3.5zM-9 -7v14c0 2 4 3.5 9 3.5s9-1.5 9-3.5v-14M-9 0c0 2 4 3.5 9 3.5s9-1.5 9-3.5';
const translate = (x, y) => `${Css.translate}${x}${Css.space}${y}${Css.closeParen}`;

function silos(snapshot) {
    const members = snapshot.activeSilos ?? [];
    return members.map(name => ({
        title: name === snapshot.siloName ? Text.thisNode : Text.silo,
        sub: name,
        self: name === snapshot.siloName
    })).sort((left, right) => Number(right.self) - Number(left.self));
}

function drawNode(root, slot, point, layout, mini) {
    const classes = [Css.node, slot.self ? Css.self : Css.empty].join(Css.space).trim();
    const group = svg(Css.g, { class: classes, transform: translate(point[Config.zero], point[Config.one]) }, root);
    svg(Css.circle, { r: layout.radius }, group);
    if (!mini) svg(Css.path, { d: glyphPath, class: Css.glyph }, group);
    svg(Css.text, { y: layout.label }, group).textContent = mini ? slot.sub : slot.title;
    if (!mini) svg(Css.text, { y: layout.sub, class: Css.sub }, group).textContent = slot.sub;
}

function drawTopology(id, snapshot, layout, mini) {
    const root = el(id);
    root.replaceChildren();
    const slots = silos(snapshot);
    const columns = Math.max(Config.one, Math.ceil(Math.sqrt(slots.length)));
    const rows = Math.max(Config.one, Math.ceil(slots.length / columns));
    const cellWidth = layout.width / columns;
    const actual = { ...layout, radius: Math.min(layout.radius, cellWidth / 4) };
    root.setAttribute('viewBox', `0 0 ${layout.width} ${rows * layout.cellHeight}`);
    slots.forEach((slot, index) => drawNode(root, slot,
        [(index % columns + 0.5) * cellWidth, (Math.floor(index / columns) + 0.3) * layout.cellHeight], actual, mini));
}

function facts(target, pairs) {
    const host = el(target);
    host.replaceChildren();
    pairs.forEach(([label, value]) => {
        const pair = make(Dom.div, Text.empty);
        pair.append(make(Dom.dt, label), make(Dom.dd, value === null || value === undefined ? Text.dash : String(value)));
        host.append(pair);
    });
}

function summary(snapshot) {
    const node = snapshot.node;
    write(Id.nodeName, Text.node + shortId(node.nodeId));
    write(Id.nodeSummary, `${node.siloCount}${Text.silos}${Text.dot}${node.replicaCount}${Text.copies}${Text.dot}${Text.position}${node.position}`);
    const routing = el(Id.routing);
    routing.className = node.routingReady ? Css.badgeGood : Css.badgeWarn;
    routing.textContent = node.routingReady ? Text.routingReady : Text.routingPending;
    write(Id.sessionNode, Text.node + shortId(node.nodeId));
}

export function renderNodes(snapshot) {
    const node = snapshot.node;
    summary(snapshot);
    drawTopology(Id.topology, snapshot, Layout.full, false);
    drawTopology(Id.miniTopology, snapshot, Layout.mini, true);
    facts(Id.processFacts, [
        [Text.processId, node.processId],
        [Text.httpInstance, snapshot.http.processInstance],
        [Text.startedAt, date(snapshot.http.startedAt)],
        [Text.uptime, duration(Date.parse(snapshot.capturedAt) - Date.parse(snapshot.http.startedAt))],
        [Text.durability, node.durability],
        [Text.siloName, snapshot.siloName]
    ]);
    facts(Id.clusterDetails, ClusterLabels.map((label, index) => [label, [node.nodeId, node.siloCount, node.replicaCount,
        node.routingReady, node.durability, node.position, node.readGeneration, node.processId, node.incarnation,
        snapshot.http.processInstance, date(snapshot.capturedAt), bytes(snapshot.admission.usage.retainedBytes)][index]]));
}

export function clearNodes() {
    write(Id.nodeName, Text.noNode);
    write(Id.nodeSummary, Text.nodeHint);
    write(Id.sessionNode, Text.notConnected);
    const routing = el(Id.routing);
    routing.className = Css.badge;
    routing.textContent = Text.awaitingRouting;
    [Id.topology, Id.miniTopology, Id.processFacts, Id.clusterDetails].forEach(id => el(id).replaceChildren());
}
