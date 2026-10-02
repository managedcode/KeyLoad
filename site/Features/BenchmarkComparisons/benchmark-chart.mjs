import { CONFIG, DOM, TEXT } from './contracts.mjs';
import { colors, metrics } from './measurements.mjs';

const TAGS = Object.freeze({ div: 'div', span: 'span', small: 'small' });
const SCENE_TEXT = Object.freeze({ keyLoad: 'KeyLoad' });
const METRIC_IDS = Object.freeze({ errors: 'errors' });
const ROW_FIELDS = Object.freeze({ name: 'name', value: 'value', status: 'status', min: 'min', max: 'max' });
const SCALE = Object.freeze({ zero: 0, one: 1, axisSteps: CONFIG.axisTicks, two: 2, floor: 1, precisionThreshold: 10 });
const EMPTY = '';
const ARIA = Object.freeze({ hidden: 'true' });

function element(documentRef, tag, className, text) {
  const node = documentRef.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function formatNumber(value, decimals = SCALE.two) {
  if (value === null || !Number.isFinite(value)) return TEXT.unrecorded;
  return new Intl.NumberFormat(CONFIG.numberLocale, { maximumFractionDigits: decimals }).format(value);
}

function scale(value, maximum, logarithmic) {
  const linear = value / maximum;
  if (!logarithmic) return linear * CONFIG.units.percent;
  return Math.log10(SCALE.one + value) / Math.log10(SCALE.one + maximum) * CONFIG.units.percent;
}

function createDot(documentRef, name) {
  const dot = element(documentRef, TAGS.span, DOM.classes.dot);
  dot.style.setProperty(DOM.properties.engine, colors[name]);
  dot.setAttribute(DOM.attributes.hidden, 'true');
  return dot;
}

function createBar(documentRef, row, metric, maximum, logarithmic, repetition, metricName) {
  const classes = [DOM.classes.row];
  if (row[ROW_FIELDS.name] === SCENE_TEXT.keyLoad) classes.push(DOM.classes.keyload);
  if (row[ROW_FIELDS.value] === null) classes.push(DOM.classes.unsupported);
  const bar = element(documentRef, TAGS.div, classes.join(' '));
  bar.style.setProperty(DOM.properties.engine, colors[row[ROW_FIELDS.name]]);
  const label = element(documentRef, TAGS.span, DOM.classes.label, row[ROW_FIELDS.name]);
  label.prepend(createDot(documentRef, row[ROW_FIELDS.name]));
  const track = element(documentRef, TAGS.div, DOM.classes.track);
  track.setAttribute(DOM.attributes.hidden, ARIA.hidden);
  const value = element(documentRef, TAGS.div, DOM.classes.value);
  if (row[ROW_FIELDS.value] === null) value.textContent = row[ROW_FIELDS.status] === CONFIG.failed ? TEXT.failed : TEXT.unsupported;
  else {
    const fill = element(documentRef, TAGS.div, DOM.classes.fill);
    fill.style.setProperty(DOM.properties.width, `${scale(row[ROW_FIELDS.value], maximum, logarithmic)}${metrics.errors.unit}`);
    track.append(fill);
    addRange(documentRef, track, row, maximum, logarithmic, repetition, metricName);
    value.append(documentRef.createTextNode(formatNumber(row[ROW_FIELDS.value])), element(documentRef, TAGS.small, EMPTY, metric.unit));
  }
  bar.append(label, track, value);
  return bar;
}

function addRange(documentRef, track, row, maximum, logarithmic, repetition, metricName) {
  if (repetition !== CONFIG.median || row[ROW_FIELDS.min] === row[ROW_FIELDS.max] || metricName === METRIC_IDS.errors) return;
  const range = element(documentRef, TAGS.div, DOM.classes.range);
  range.style.setProperty(DOM.properties.minimum, `${scale(row[ROW_FIELDS.min], maximum, logarithmic)}${metrics.errors.unit}`);
  range.style.setProperty(DOM.properties.range,
    `${scale(row[ROW_FIELDS.max], maximum, logarithmic) - scale(row[ROW_FIELDS.min], maximum, logarithmic)}${metrics.errors.unit}`);
  track.append(range);
}

function renderAxis(documentRef, maximum, logarithmic) {
  const axis = element(documentRef, TAGS.div, DOM.classes.axis);
  const labels = element(documentRef, TAGS.div, DOM.classes.axisLabels);
  const precision = maximum < SCALE.precisionThreshold ? SCALE.two : SCALE.zero;
  for (let tick = SCALE.zero; tick <= SCALE.axisSteps; tick += SCALE.one) {
    const fraction = tick / SCALE.axisSteps;
    const value = logarithmic ? Math.pow(SCALE.one + maximum, fraction) - SCALE.one : maximum * fraction;
    labels.append(element(documentRef, TAGS.span, EMPTY, formatNumber(value, precision)));
  }
  axis.append(element(documentRef, TAGS.span, EMPTY), labels, element(documentRef, TAGS.span, EMPTY));
  return axis;
}

function metricNote(metricName, repetition) {
  if (metrics[metricName].client) return TEXT.generatorNote;
  if (metricName === METRIC_IDS.errors) return TEXT.failureNote;
  return repetition === CONFIG.median ? TEXT.medianNote : TEXT.repetitionNote;
}

export function renderChart({ chart, title, direction, note, rows, metricName, repetition, logarithmic }) {
  const documentRef = chart.ownerDocument;
  const metric = metrics[metricName];
  title.textContent = metric.title;
  direction.textContent = metric.direction;
  note.textContent = metricNote(metricName, repetition);
  const maximum = Math.max(SCALE.floor, ...rows.filter(row => row[ROW_FIELDS.value] !== null)
    .map(row => row[ROW_FIELDS.max] ?? row[ROW_FIELDS.value]));
  const bars = rows.map(row => createBar(documentRef, row, metric, maximum, logarithmic, repetition, metricName));
  chart.replaceChildren(...bars, renderAxis(documentRef, maximum, logarithmic));
}
