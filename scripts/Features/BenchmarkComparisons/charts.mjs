import { caseStatus, chartColor, chartLayout, chartMetric, emptyString, legacyScenarioNames, legacyTargetNames, queueStage, scenarioName, scenarioNames, schemaField, summaryField, targetNames } from './contracts.mjs';
import { messages } from './messages.mjs';

const escapeMap = Object.freeze({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' });
const escapePattern = /[&<>"']/g;
const numberPrecision = 3;
const svgClass = Object.freeze({ axis: 'axis', grid: 'grid', range: 'range', bar: 'bar', unavailable: 'unavailable', small: 'small' });
const metricDefinitions = Object.freeze({
  [chartMetric.throughput]: Object.freeze({ title: messages.chartLabels.metrics.throughputTitle, unit: messages.chartLabels.metrics.throughputUnit, field: schemaField.usefulOperationsPerSecond, colors: [chartColor.teal] }),
  [chartMetric.p99]: Object.freeze({ title: messages.chartLabels.metrics.p99Title, unit: messages.chartLabels.metrics.latencyUnit, field: schemaField.p99Ms, colors: [chartColor.blue] }),
});
const queueStages = Object.freeze([
  Object.freeze({ key: queueStage.enqueue, label: messages.chartLabels.queueStages.enqueue, color: chartColor.teal }),
  Object.freeze({ key: queueStage.receive, label: messages.chartLabels.queueStages.receive, color: chartColor.blue }),
  Object.freeze({ key: queueStage.ack, label: messages.chartLabels.queueStages.ack, color: chartColor.amber }),
]);
const svgTemplates = Object.freeze({
  metadata: value => `<metadata>${value}</metadata>`,
  close: '</svg>',
  document: (width, height, title, description, meta, smallFont) => `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-labelledby="chart-title chart-description"><title id="chart-title">${title}</title><desc id="chart-description">${description}</desc>${meta}<style>text{font-family:Arial,sans-serif;fill:#17252d}.axis{stroke:#73828b;stroke-width:1}.grid{stroke:#d9e0e4;stroke-width:1}.range{stroke:#23343b;stroke-width:2}.bar{opacity:.88}.unavailable{font-size:9px;fill:#69767d}.small{font-size:${smallFont}px}</style>`,
  scenarioTitle: (x, y, label, fontSize) => `<text x="${x}" y="${y}" font-size="${fontSize}" font-weight="700">${label}</text>`,
  subtitle: (x, y, label) => `<text x="${x}" y="${y}" class="small">${label}</text>`,
  gridLine: (className, x1, y1, x2, y2) => `<line class="${className}" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}"/>`,
  tickLabel: (className, x, y, value) => `<text class="${className}" text-anchor="end" x="${x}" y="${y}">${value}</text>`,
  axes: (className, left, top, bottom, right) => `<line class="${className}" x1="${left}" y1="${top}" x2="${left}" y2="${bottom}"/><line class="${className}" x1="${left}" y1="${bottom}" x2="${right}" y2="${bottom}"/>`,
  unavailable: (className, x, y, label) => `<text class="${className}" text-anchor="middle" x="${x}" y="${y}">${label}</text>`,
  bar: (className, color, x, y, width, height) => `<rect class="${className}" fill="${color}" x="${x}" y="${y}" width="${width}" height="${height}"/>`,
  range: (className, center, maximum, minimum, capLeft, capRight) => `<line class="${className}" x1="${center}" y1="${maximum}" x2="${center}" y2="${minimum}"/><line class="${className}" x1="${capLeft}" y1="${maximum}" x2="${capRight}" y2="${maximum}"/><line class="${className}" x1="${capLeft}" y1="${minimum}" x2="${capRight}" y2="${minimum}"/>`,
  rotatedLabel: (className, x, y, rotation, label) => `<text class="${className}" text-anchor="end" transform="translate(${x} ${y}) rotate(${rotation})">${label}</text>`,
  queueBarAndRange: (className, color, x, y, width, height, center, maximum, minimum) => `<rect class="${className}" fill="${color}" x="${x}" y="${y}" width="${width}" height="${height}"/><line class="${svgClass.range}" x1="${center}" y1="${maximum}" x2="${center}" y2="${minimum}"/>`,
  legend: (x, y, size, color, labelX, labelY, className, label) => `<rect x="${x}" y="${y}" width="${size}" height="${size}" fill="${color}"/><text class="${className}" x="${labelX}" y="${labelY}">${label}</text>`,
});

function escapeXml(value) {
  return String(value).replace(escapePattern, character => escapeMap[character]);
}

function median(values) {
  const ordered = [...values].sort((left, right) => left - right);
  const midpoint = Math.floor(ordered.length / 2);
  return ordered.length % 2 === 0 ? (ordered[midpoint - 1] + ordered[midpoint]) / 2 : ordered[midpoint];
}

function rangeStats(values) {
  return { median: median(values), minimum: Math.min(...values), maximum: Math.max(...values) };
}

function caseAggregate(report, targetValue, scenarioValue) {
  const repetitions = report[schemaField.cases].filter(item => item[schemaField.target] === targetValue && item[schemaField.scenario] === scenarioValue);
  const measured = repetitions.filter(item => item[schemaField.status] === caseStatus.measured && item[schemaField.measurement]);
  if (measured.length === 0) return { [schemaField.status]: caseStatus.unavailable, [schemaField.repetitions]: repetitions.length };
  const throughput = measured.map(item => item[schemaField.measurement][schemaField.usefulOperationsPerSecond]);
  const p99 = measured.map(item => item[schemaField.measurement][schemaField.latency][schemaField.p99Ms]);
  return {
    [schemaField.status]: caseStatus.measured,
    [schemaField.repetitions]: measured.length,
    [summaryField.throughput]: rangeStats(throughput),
    [summaryField.p99Ms]: rangeStats(p99),
    [summaryField.queueP99Ms]: scenarioValue === scenarioName.queueCycle ? Object.fromEntries(queueStages.map(stage => [stage.key,
      rangeStats(measured.map(item => item[schemaField.measurement][stage.key][schemaField.p99Ms]))])) : null,
  };
}

export function summarizeProfile(report, profileName, topologyLabel, legacy = false) {
  const targets = legacy ? legacyTargetNames : targetNames;
  const scenarios = legacy ? legacyScenarioNames : scenarioNames;
  const cases = [];
  for (const target of targets) {
    for (const scenario of scenarios) cases.push({ target, scenario, ...caseAggregate(report, target, scenario) });
  }
  return {
    [schemaField.profile]: profileName,
    [schemaField.legacyBaseline]: legacy,
    [schemaField.qualifiesSchema3]: !legacy,
    [schemaField.sourceRevision]: report[schemaField.sourceRevision],
    [schemaField.topology]: topologyLabel,
    [schemaField.corpusSha256]: report[schemaField.datasetSha256],
    [schemaField.options]: report[schemaField.options],
    [schemaField.targets]: targets,
    [schemaField.scenarios]: scenarios,
    [summaryField.targetCount]: report[schemaField.targets].length,
    [summaryField.scenarioCount]: scenarios.length,
    [schemaField.repetitions]: report[schemaField.options][schemaField.repetitions],
    [summaryField.supportedCaseCount]: report[schemaField.cases].filter(item => item[schemaField.status] === caseStatus.measured).length,
    [summaryField.unavailableCaseCount]: report[schemaField.cases].filter(item => item[schemaField.status] === caseStatus.unsupported).length,
    [schemaField.cases]: cases,
  };
}

function metadataBlock(profile, metadata, rawUrl, corpusHash, topology) {
  const guarantees = metadata[schemaField.targets].map(target => `${target[schemaField.name]}${messages.chart.guaranteeWrite}${target[schemaField.writeAcknowledgement]}${messages.chart.guaranteeRead}${target[schemaField.readContract]}`).join(messages.chart.lineBreak);
  const details = messages.chart.metadataDetails(metadata, profile, topology, corpusHash, rawUrl, guarantees);
  return svgTemplates.metadata(escapeXml(details));
}

function documentStart(width, height, title, description, meta) {
  return svgTemplates.document(width, height, escapeXml(title), escapeXml(description), meta, chartLayout.smallFont);
}

function aggregateFor(summary, target, scenario, metricName) {
  const item = summary[schemaField.cases].find(entry => entry[schemaField.target] === target && entry[schemaField.scenario] === scenario);
  if (!item || item[schemaField.status] !== caseStatus.measured) return null;
  return metricName === chartMetric.throughput ? item[summaryField.throughput] : item[summaryField.p99Ms];
}

function panelBars(summary, scenario, metricName, originX, originY, panelWidth, panelHeight) {
  const metric = metricDefinitions[metricName];
  const left = originX + chartLayout.chartLeft;
  const top = originY + chartLayout.chartTop;
  const plotWidth = panelWidth - chartLayout.chartLeft - chartLayout.panelGap;
  const plotHeight = chartLayout.plotHeight;
  const allStats = summary[schemaField.targets].map(target => aggregateFor(summary, target, scenario, metricName)).filter(Boolean);
  const maxValue = Math.max(chartLayout.minimumAxisMaximum, ...allStats.map(item => item.maximum));
  const barSlot = plotWidth / summary[schemaField.targets].length;
  const barWidth = Math.min(chartLayout.barMaximumWidth, barSlot * chartLayout.barSlotFraction);
  const bottom = top + plotHeight;
  const elements = [svgTemplates.scenarioTitle(originX + chartLayout.panelTitleOffsetX, originY + chartLayout.panelTitleOffsetY, escapeXml(messages.chartLabels.scenarios[scenario]), chartLayout.panelTitleFont)];
  for (let tick = 0; tick <= chartLayout.scenarioTickCount; tick++) {
    const y = bottom - plotHeight * tick / chartLayout.scenarioTickCount;
    elements.push(svgTemplates.gridLine(svgClass.grid, left, y, left + plotWidth, y));
    elements.push(svgTemplates.tickLabel(svgClass.small, left - chartLayout.scenarioTickLabelOffsetX, y + chartLayout.scenarioTickLabelOffsetY, (maxValue * tick / chartLayout.scenarioTickCount).toPrecision(numberPrecision)));
  }
  elements.push(svgTemplates.axes(svgClass.axis, left, top, bottom, left + plotWidth));
  summary[schemaField.targets].forEach((target, index) => {
    const center = left + barSlot * (index + chartLayout.barCenterFraction);
    const stats = aggregateFor(summary, target, scenario, metricName);
    if (!stats) {
      elements.push(svgTemplates.unavailable(svgClass.unavailable, center, bottom - chartLayout.unavailableScenarioOffsetY, messages.chart.unavailable));
    } else {
      const yMedian = bottom - stats.median / maxValue * plotHeight;
      const yMinimum = bottom - stats.minimum / maxValue * plotHeight;
      const yMaximum = bottom - stats.maximum / maxValue * plotHeight;
      elements.push(svgTemplates.bar(svgClass.bar, metric.colors[0], center - barWidth / 2, yMedian, barWidth, bottom - yMedian));
      elements.push(svgTemplates.range(svgClass.range, center, yMaximum, yMinimum, center - chartLayout.rangeCapHalfWidth, center + chartLayout.rangeCapHalfWidth));
    }
    elements.push(svgTemplates.rotatedLabel(svgClass.small, center + chartLayout.scenarioLabelOffsetX, bottom + chartLayout.scenarioLabelOffsetY, chartLayout.scenarioLabelRotation, escapeXml(messages.chartLabels.targets[target])));
  });
  return elements.join(emptyString);
}

export function renderScenarioChart(summary, metricName, metadata, rawUrl) {
  const metric = metricDefinitions[metricName];
  const width = chartLayout.width;
  const height = chartLayout.height;
  const profileName = summary[schemaField.profile];
  const start = documentStart(width, height, `${profileName}${messages.chart.titleSeparator}${metric.title}`, `${metric.title}${messages.chart.scenarioDescriptionSuffix}`, metadataBlock(profileName, metadata, rawUrl, summary[schemaField.corpusSha256], summary[schemaField.topology]));
  const elements = [svgTemplates.scenarioTitle(chartLayout.chartTitleX, chartLayout.chartTitleY, `${escapeXml(metric.title)}${messages.chart.titleJoiner}${escapeXml(profileName)}`, chartLayout.titleFont), svgTemplates.subtitle(chartLayout.chartTitleX, chartLayout.chartSubtitleY, `${escapeXml(messages.chart.dataHeading)} (${escapeXml(metric.unit)})${messages.chart.unsupportedCaseNote}`)];
  summary[schemaField.scenarios].forEach((scenario, index) => {
    const column = index % chartLayout.panelColumns;
    const row = Math.floor(index / chartLayout.panelColumns);
    const x = chartLayout.panelOriginX + column * (chartLayout.panelWidth + chartLayout.panelGap);
    const y = chartLayout.panelOriginY + row * chartLayout.panelHeight;
    elements.push(panelBars(summary, scenario, metricName, x, y, chartLayout.panelWidth, chartLayout.panelHeight));
  });
  return `${start}${elements.join(emptyString)}${svgTemplates.close}`;
}

function queueValue(summary, target, stage) {
  const item = summary[schemaField.cases].find(entry => entry[schemaField.target] === target && entry[schemaField.scenario] === scenarioName.queueCycle);
  return item?.[schemaField.status] === caseStatus.measured ? item[summaryField.queueP99Ms][stage] : null;
}

export function renderQueueChart(summary, metadata, rawUrl) {
  const width = chartLayout.queueWidth;
  const height = chartLayout.queueHeight;
  const profileName = summary[schemaField.profile];
  const title = `${profileName}${messages.chart.titleSeparator}${messages.chart.queueTitle}`;
  const start = documentStart(width, height, title, messages.chart.queueDescription, metadataBlock(profileName, metadata, rawUrl, summary[schemaField.corpusSha256], summary[schemaField.topology]));
  const left = chartLayout.queueLeft;
  const top = chartLayout.queueTop;
  const plotHeight = chartLayout.queuePlotHeight;
  const plotWidth = width - left - chartLayout.queueRight;
  const bottom = top + plotHeight;
  const entries = summary[schemaField.targets].flatMap(target => queueStages.map(stage => queueValue(summary, target, stage.key))).filter(Boolean);
  const maxValue = Math.max(chartLayout.minimumAxisMaximum, ...entries.map(item => item.maximum));
  const groupWidth = plotWidth / summary[schemaField.targets].length;
  const barWidth = chartLayout.queueBarWidth;
  const elements = [svgTemplates.scenarioTitle(chartLayout.queueTitleX, chartLayout.queueTitleY, escapeXml(title), chartLayout.titleFont), svgTemplates.subtitle(chartLayout.queueTitleX, chartLayout.queueSubtitleY, `${escapeXml(messages.chart.dataHeading)} (${messages.chart.queueUnit}); ${messages.chart.queueUnsupportedNote}`)];
  for (let tick = 0; tick <= chartLayout.queueTickCount; tick++) {
    const y = bottom - plotHeight * tick / chartLayout.queueTickCount;
    elements.push(svgTemplates.gridLine(svgClass.grid, left, y, left + plotWidth, y));
    elements.push(svgTemplates.tickLabel(svgClass.small, left - chartLayout.queueTickLabelOffsetX, y + chartLayout.scenarioTickLabelOffsetY, (maxValue * tick / chartLayout.queueTickCount).toPrecision(numberPrecision)));
  }
  elements.push(svgTemplates.axes(svgClass.axis, left, top, bottom, left + plotWidth));
  summary[schemaField.targets].forEach((target, targetIndex) => {
    const groupStart = left + targetIndex * groupWidth;
    const hasStage = queueStages.some(stage => queueValue(summary, target, stage.key));
    if (!hasStage) elements.push(svgTemplates.unavailable(svgClass.unavailable, groupStart + groupWidth / 2, bottom - chartLayout.unavailableQueueOffsetY, messages.chart.unavailable));
    queueStages.forEach((stage, stageIndex) => {
      const stats = queueValue(summary, target, stage.key);
      if (!stats) return;
      const center = groupStart + groupWidth / 2 + (stageIndex - chartLayout.queueStageCenterOffset) * (barWidth + chartLayout.queueBarGap);
      const yMedian = bottom - stats.median / maxValue * plotHeight;
      const yMinimum = bottom - stats.minimum / maxValue * plotHeight;
      const yMaximum = bottom - stats.maximum / maxValue * plotHeight;
      elements.push(svgTemplates.queueBarAndRange(svgClass.bar, stage.color, center - barWidth / 2, yMedian, barWidth, bottom - yMedian, center, yMaximum, yMinimum));
    });
    elements.push(svgTemplates.rotatedLabel(svgClass.small, groupStart + groupWidth / 2 + chartLayout.queueLabelOffsetX, bottom + chartLayout.queueLabelOffsetY, chartLayout.queueLabelRotation, escapeXml(messages.chartLabels.targets[target])));
  });
  const legend = queueStages.map((stage, index) => svgTemplates.legend(left + index * chartLayout.queueLegendSpacing, height - chartLayout.queueLegendY, chartLayout.queueLegendSwatchSize, stage.color, left + index * chartLayout.queueLegendSpacing + chartLayout.queueLegendTextOffsetX, height - chartLayout.queueLegendTextOffsetY, svgClass.small, stage.label)).join(emptyString);
  return `${start}${elements.join(emptyString)}${legend}${svgTemplates.close}`;
}
