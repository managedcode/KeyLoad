import { ClusterLabels, Config, Css, Dom, Id } from './constants.js';
import { Text } from './text.js';
import { el, make, svg, write } from './dom.js';
import { bytes, date, duration, shortId } from './format.js';

const Layout = Object.freeze({
    full: { radius: 34, crown: 9, label: 58, sub: 76, points: [[140, 110], [420, 50], [420, 170]] },
    mini: { radius: 15, crown: 5, label: 33, sub: 46, points: [[40, 30], [120, 30], [200, 30]] }
});
const glyphPath = 'M-9 -7c0-2 4-3.5 9-3.5s9 1.5 9 3.5-4 3.5-9 3.5-9-1.5-9-3.5zM-9 -7v14c0 2 4 3.5 9 3.5s9-1.5 9-3.5v-14M-9 0c0 2 4 3.5 9 3.5s9-1.5 9-3.5';
const translate = (x, y) => `${Css.translate}${x}${Css.space}${y}${Css.closeParen}`;

function voters(snapshot) {
    const node = snapshot.node;
    const members = snapshot.voters ?? [];
    if (!members.length) return [{ title: Text.thisNode, sub: shortId(node.nodeId), self: true, leader: false }];
    const slots = members.map(id => ({
        title: id === snapshot.localVoter ? Text.thisNode : id === node.leader ? Text.leader : Text.peer,
        sub: shortId(id),
        self: id === snapshot.localVoter,
        leader: id === node.leader
    }));
    const rank = slot => slot.self ? Config.zero : slot.leader ? Config.one : Config.two;
    return slots.sort((left, right) => rank(left) - rank(right));
}

function drawNode(root, slot, point, layout, mini) {
    const classes = [Css.node, slot.self ? Css.self : Css.empty, slot.leader ? Css.leader : Css.empty].join(Css.space).trim();
    const group = svg(Css.g, { class: classes, transform: translate(point[Config.zero], point[Config.one]) }, root);
    svg(Css.circle, { r: layout.radius }, group);
    if (!mini) svg(Css.path, { d: glyphPath, class: Css.glyph }, group);
    if (slot.leader) svg(Css.circle, { class: Css.crown, cx: layout.radius * 0.72, cy: -layout.radius * 0.72, r: layout.crown }, group);
    svg(Css.text, { y: layout.label }, group).textContent = slot.title;
    if (!mini) svg(Css.text, { y: layout.sub, class: Css.sub }, group).textContent = slot.sub;
}

function drawTopology(id, snapshot, layout, mini) {
    const root = el(id);
    root.replaceChildren();
    const slots = voters(snapshot).slice(Config.zero, layout.points.length);
    const points = layout.points.slice(Config.zero, slots.length);
    points.forEach((from, index) => points.slice(index + Config.one).forEach(to =>
        svg(Css.line, { x1: from[0], y1: from[1], x2: to[0], y2: to[1] }, root)));
    slots.forEach((slot, index) => drawNode(root, slot, points[index], layout, mini));
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
    write(Id.nodeSummary, `${node.voters}${Text.voters}${Text.dot}${Text.applied}${node.applied}${Text.dot}${Text.generation}${node.readGeneration}`);
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
        [Text.localVoter, snapshot.localVoter],
        [Text.leader, node.leader ?? Text.unknownLeader]
    ]);
    facts(Id.clusterDetails, ClusterLabels.map((label, index) => [label, [node.nodeId, node.leader, node.voters,
        node.routingReady, node.durability, node.applied, node.readGeneration, node.processId, node.incarnation,
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
