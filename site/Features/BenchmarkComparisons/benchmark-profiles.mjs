import { CONFIG, CONDITIONS, DOM, PROFILE_LABELS, TEXT } from './contracts.mjs';
import { colors, metrics } from './measurements.mjs';

const TAGS = Object.freeze({ article: 'article', heading: 'h3', paragraph: 'p', details: 'details', summary: 'summary',
  list: 'dl', term: 'dt', definition: 'dd', span: 'span', strong: 'strong' });
const PROFILE_FIELDS = Object.freeze({ name: 'name', version: 'version', topology: 'topology', write: 'writeAcknowledgement',
  reads: 'readContract', transport: 'transport', authorization: 'authorization', image: 'image' });
const OPTION_FIELDS = Object.freeze({ documents: 'documents', payload: 'payloadBytes', concurrency: 'concurrency',
  operations: 'operations', warmup: 'warmup', repetitions: 'repetitions', graphDepth: 'graphDepth',
  graphVertices: 'graphVertices', graphFanOut: 'graphFanOut', dimensions: 'dimensions', topK: 'topK' });
const GRAPH_SCENARIOS = Object.freeze({ neighbors: 'GraphNeighbors', traverse: 'GraphTraverse', vector: 'VectorExact' });
const GEOMETRY = Object.freeze({ decimals: 2, bytesPerKiB: CONFIG.units.bytesPerKiB, one: 1 });
const TEXT_VALUES = Object.freeze({ graph: 'Graph', vectors: 'Vectors', vertices: 'vertices', fanOut: 'fan-out', hops: 'hops',
  dimensions: 'dimensions', topK: 'top-', labelSeparator: ' ' });
const EMPTY = '';
const ARIA = Object.freeze({ hidden: 'true' });

function element(documentRef, name, className, text) {
  const node = documentRef.createElement(name);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function createProfileCard(documentRef, target) {
  const card = element(documentRef, TAGS.article, DOM.classes.card);
  const heading = element(documentRef, TAGS.heading, EMPTY, target[PROFILE_FIELDS.name]);
  heading.prepend(element(documentRef, TAGS.span, DOM.classes.dot));
  heading.firstElementChild.style.setProperty(DOM.properties.engine, colors[target[PROFILE_FIELDS.name]]);
  heading.firstElementChild.setAttribute(DOM.attributes.hidden, ARIA.hidden);
  card.append(heading, element(documentRef, TAGS.paragraph, DOM.classes.version, target[PROFILE_FIELDS.version]),
    element(documentRef, TAGS.paragraph, EMPTY, target[PROFILE_FIELDS.topology]));
  const details = element(documentRef, TAGS.details, EMPTY);
  details.append(element(documentRef, TAGS.summary, EMPTY, PROFILE_LABELS.summary));
  const list = element(documentRef, TAGS.list, EMPTY);
  const fields = [
    [PROFILE_LABELS.write, target[PROFILE_FIELDS.write]], [PROFILE_LABELS.reads, target[PROFILE_FIELDS.reads]],
    [PROFILE_LABELS.transport, target[PROFILE_FIELDS.transport]], [PROFILE_LABELS.authorization, target[PROFILE_FIELDS.authorization]],
    [PROFILE_LABELS.image, target[PROFILE_FIELDS.image] ?? TEXT.sourceCheckout],
  ];
  for (const [label, value] of fields) list.append(element(documentRef, TAGS.term, EMPTY, label),
    element(documentRef, TAGS.definition, EMPTY, value));
  details.append(list);
  card.append(details);
  return card;
}

export function renderProfiles(container, targets) {
  const documentRef = container.ownerDocument;
  container.replaceChildren(...targets.map(target => createProfileCard(documentRef, target)));
}

function formatNumber(value) {
  return new Intl.NumberFormat(CONFIG.numberLocale, { maximumFractionDigits: GEOMETRY.decimals }).format(value);
}

function addCondition(documentRef, container, label, value) {
  const item = element(documentRef, TAGS.span, EMPTY, `${label}${TEXT_VALUES.labelSeparator}`);
  item.append(element(documentRef, TAGS.strong, EMPTY, value));
  container.append(item);
}

export function renderConditions(container, options, scenario) {
  const documentRef = container.ownerDocument;
  const values = [
    [CONDITIONS.documents, formatNumber(options[OPTION_FIELDS.documents])],
    [CONDITIONS.payload, `${formatNumber(options[OPTION_FIELDS.payload] / GEOMETRY.bytesPerKiB)} ${metrics.alloc.unit}`],
    [CONDITIONS.concurrency, String(options[OPTION_FIELDS.concurrency])],
    [CONDITIONS.attempts, String(options[OPTION_FIELDS.operations])],
    [CONDITIONS.warmup, String(options[OPTION_FIELDS.warmup])], [CONDITIONS.repetitions, String(options[OPTION_FIELDS.repetitions])],
  ];
  if (scenario === GRAPH_SCENARIOS.neighbors || scenario === GRAPH_SCENARIOS.traverse) {
    const hops = scenario === GRAPH_SCENARIOS.neighbors ? GEOMETRY.one : options[OPTION_FIELDS.graphDepth];
    values.push([TEXT_VALUES.graph, `${Math.min(options[OPTION_FIELDS.documents], options[OPTION_FIELDS.graphVertices])} ${TEXT_VALUES.vertices} · ${TEXT_VALUES.fanOut} ${options[OPTION_FIELDS.graphFanOut]} · ${hops} ${TEXT_VALUES.hops}`]);
  }
  if (scenario === GRAPH_SCENARIOS.vector) values.push([TEXT_VALUES.vectors,
    `${options[OPTION_FIELDS.dimensions]} ${TEXT_VALUES.dimensions} · ${TEXT_VALUES.topK}${options[OPTION_FIELDS.topK]}`]);
  container.replaceChildren();
  for (const [label, value] of values) addCondition(documentRef, container, label, value);
}
