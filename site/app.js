import { scenarios, colors, metrics, selectedRows } from "./measurements.mjs";

const $ = id => document.getElementById(id);
const state = { catalog: null, entry: null, report: null, scenario: "PointRead", metric: "throughput", repetition: "median" };
let loadingVersion = 0;
const number = (value, decimals = 2) => value === null || !Number.isFinite(value) ? "—" : new Intl.NumberFormat("en", { maximumFractionDigits: decimals }).format(value);
const element = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
};
const dot = name => { const node = element("span", "engine-dot"); node.style.setProperty("--engine", colors[name] ?? "#788570"); node.setAttribute("aria-hidden", "true"); return node; };
function fail(message) {
  state.report = null; state.entry = null;
  $("load-error").textContent = message; $("load-error").hidden = false;
  $("chart").replaceChildren(element("p", "empty-state", "Measurements could not be loaded. Open the measurement evidence or try again."));
  $("result-table").replaceChildren();
  $("conditions").replaceChildren(); $("engine-profiles").replaceChildren();
  $("published-date").textContent = "Measurements unavailable"; $("host-summary").textContent = ""; $("revision-summary").textContent = "";
  for (const id of ["json-download", "csv-download", "report-download"]) { $(id).removeAttribute("href"); $(id).setAttribute("aria-disabled", "true"); }
}
async function loadReport(entry) {
  const version = ++loadingVersion;
  state.report = null;
  $("load-error").hidden = true;
  $("chart").replaceChildren(element("p", "empty-state", "Loading this workload profile…"));
  try {
    const url = new URL(entry.report, new URL("./data/", window.location.href));
    const response = await fetch(url, { cache: "no-cache" });
    if (!response.ok) throw new Error("Report unavailable");
    const report = await response.json();
    if (version !== loadingVersion) return;
    if (report.schemaVersion !== 2 || !Array.isArray(report.cases) || !Array.isArray(report.targets)) throw new Error("Report schema unavailable");
    state.entry = entry; state.report = report; state.repetition = "median";
    $("repetition").replaceChildren(new Option("Median of repetitions", "median"));
    for (let i = 0; i < report.options.repetitions; i++) $("repetition").add(new Option(`Repetition ${i + 1}`, String(i)));
    $("published-date").textContent = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" }).format(new Date(report.startedAt)) + " UTC";
    $("host-summary").textContent = `${report.hostOs} · ${report.architecture} · ${report.logicalProcessors} logical CPUs · ${report.runtime}`;
    $("revision-summary").textContent = `Source ${report.sourceRevision?.slice(0, 12) ?? "unrecorded"} · corpus ${report.datasetSha256.slice(0, 12)}`;
    for (const [id, file] of [["json-download", "results.json"], ["csv-download", "samples.csv"], ["report-download", "results.md"]]) { $(id).href = new URL(file, url).href; $(id).removeAttribute("aria-disabled"); }
    $("workflow-link").href = entry.evidenceUrl ?? "https://github.com/managedcode/KeyLoad/actions";
    renderProfiles(); render();
  } catch {
    if (version === loadingVersion) fail("The selected published report is unavailable. No substitute measurements are displayed.");
  }
}
function renderProfiles() {
  $("engine-profiles").replaceChildren(...state.report.targets.map(target => {
    const card = element("article", "engine-card");
    const title = element("h3", "", target.name); title.prepend(dot(target.name));
    card.append(title, element("p", "version", target.version), element("p", "", target.topology));
    const details = element("details"); details.append(element("summary", "", "Guarantees & configuration"));
    const list = element("dl");
    for (const [name, value] of [["Write acknowledgement", target.writeAcknowledgement], ["Reads", target.readContract], ["Transport", target.transport], ["Authorization", target.authorization], ["Pinned image", target.image ?? "Source checkout" ]]) list.append(element("dt", "", name), element("dd", "", value));
    details.append(list); card.append(details); return card;
  }));
}
function render() {
  if (!state.report) return;
  const { report, scenario, repetition } = state;
  if (metrics[state.metric].queue && scenario !== "QueueCycle") { state.metric = "throughput"; $("metric").value = "throughput"; }
  for (const option of $("metric").options) option.disabled = Boolean(metrics[option.value].queue && scenario !== "QueueCycle");
  const metric = metrics[state.metric];
  $("chart-title").textContent = metric.title; $("direction").textContent = metric.direction;
  $("scenario-description").textContent = scenarios[scenario][1] + (scenario === "GraphTraverse" ? ` Depth: ${report.options.graphDepth} hops.` : "");
  $("results-panel").setAttribute("aria-labelledby", `tab-${scenario}`);
  for (const button of document.querySelectorAll("[data-scenario]")) {
    const active = button.dataset.scenario === scenario;
    button.setAttribute("aria-selected", String(active)); button.tabIndex = active ? 0 : -1;
  }
  const rows = selectedRows(report, scenario, repetition, state.metric);
  const max = Math.max(1, ...rows.filter(row => row.value !== null).map(row => row.max ?? row.value));
  const log = $("log-scale").checked;
  const scale = value => (log ? Math.log10(1 + value) / Math.log10(1 + max) : value / max) * 100;
  const chartRows = rows.map(row => {
    const bar = element("div", `bar-row${row.name === "KeyLoad" ? " keyload" : ""}${row.value === null ? " unsupported-row" : ""}`);
    bar.style.setProperty("--engine", colors[row.name] ?? "#788570");
    const label = element("span", "bar-label", row.name); label.prepend(dot(row.name));
    const track = element("div", "bar-track"); track.setAttribute("aria-hidden", "true");
    const value = element("div", "bar-value");
    if (row.value !== null) {
      const fill = element("div", "bar-fill"); fill.style.setProperty("--width", `${scale(row.value)}%`); track.append(fill);
      if (repetition === "median" && row.min !== row.max && state.metric !== "errors") {
        const range = element("div", "bar-range"); range.style.setProperty("--min", `${scale(row.min)}%`); range.style.setProperty("--range", `${scale(row.max) - scale(row.min)}%`); track.append(range);
      }
      value.append(document.createTextNode(number(row.value)), element("small", "", metric.unit));
    } else value.textContent = row.status === "failed" ? "Failed" : "Unsupported";
    bar.append(label, track, value); return bar;
  });
  const axis = element("div", "bar-axis"); const labels = element("div", "axis-labels");
  for (let i = 0; i <= 4; i++) labels.append(element("span", "", number(log ? Math.pow(1 + max, i / 4) - 1 : max * i / 4, max < 10 ? 2 : 0)));
  axis.append(element("span"), labels, element("span"));
  $("chart").replaceChildren(...chartRows, axis);
  $("metric-note").textContent = metric.client
    ? "These measurements cover the load generator, drivers and sampler. RSS carries process history and is sampled every 50 ms; it is not database RAM."
    : state.metric === "errors" ? "Failure rate uses every selected attempt. Failed and timed-out requests remain in the denominator."
    : repetition === "median" ? "Bars show the median of per-repetition values. Whiskers show the smallest and largest value. Latency values are per-run percentiles, not pooled percentiles."
    : "One complete repetition is selected. Setup, warmup and correctness checks are excluded from the timer.";
  const o = report.options;
  const conditions = [["Documents", number(o.documents, 0)], ["Payload", `${number(o.payloadBytes / 1024)} KiB`], ["Concurrency", String(o.concurrency)], ["Attempts/case", String(o.operations)], ["Warmup", String(o.warmup)], ["Repetitions", String(o.repetitions)]];
  if (scenario.startsWith("Graph")) conditions.push(["Graph", `${Math.min(o.documents, o.graphVertices)} vertices · fan-out ${o.graphFanOut} · ${scenario === "GraphNeighbors" ? 1 : o.graphDepth} hops`]);
  if (scenario === "VectorExact") conditions.push(["Vectors", `${o.dimensions} dimensions · top-${o.topK}`]);
  $("conditions").replaceChildren(...conditions.map(([key, value]) => { const item = element("span", "", key + " "); item.append(element("strong", "", value)); return item; }));
  $("result-table").replaceChildren(...rows.map(row => {
    const tr = element("tr", row.name === "KeyLoad" ? "keyload-table-row" : "");
    const name = element("td"); const label = element("span", "cell-engine", row.name); label.prepend(dot(row.name)); name.append(label); tr.append(name);
    for (const value of [row.throughput, row.p50, row.p95, row.p99]) tr.append(element("td", "", number(value)));
    tr.append(element("td", "", row.attempts ? `${number(row.successes, 0)} / ${number(row.attempts, 0)}` : "—"));
    const status = element("td"); status.append(element("span", `result-status ${row.status}`, row.status === "measured" ? "Verified" : row.status === "failed" ? "Failed" : "Unsupported")); tr.append(status); return tr;
  }));
}
$("profile").addEventListener("change", event => loadReport(state.catalog.runs.find(run => run.id === event.target.value)));
$("metric").addEventListener("change", event => { state.metric = event.target.value; render(); });
$("repetition").addEventListener("change", event => { state.repetition = event.target.value; render(); });
$("log-scale").addEventListener("change", render);
const tabs = [...document.querySelectorAll("[data-scenario]")];
for (const [index, tab] of tabs.entries()) {
  tab.addEventListener("click", () => { state.scenario = tab.dataset.scenario; render(); });
  tab.addEventListener("keydown", event => {
    const next = event.key === "ArrowRight" ? (index + 1) % tabs.length : event.key === "ArrowLeft" ? (index - 1 + tabs.length) % tabs.length : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
    if (next !== null) { event.preventDefault(); tabs[next].focus(); tabs[next].click(); }
  });
}
$("copy-command").addEventListener("click", async () => {
  try { await navigator.clipboard.writeText(document.querySelector(".command-box code").textContent); $("copy-command").textContent = "Copied"; }
  catch { const range = document.createRange(); range.selectNodeContents(document.querySelector(".command-box code")); const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range); $("copy-command").textContent = "Selected"; }
  setTimeout(() => { $("copy-command").textContent = "Copy"; }, 1800);
});
try {
  const response = await fetch("./data/catalog.json", { cache: "no-cache" });
  if (!response.ok) throw new Error("Catalog unavailable");
  state.catalog = await response.json();
  if (!state.catalog.runs?.length) throw new Error("No published measurements");
  $("profile").replaceChildren(...state.catalog.runs.map(run => new Option(run.label, run.id)));
  $("profile").disabled = false;
  await loadReport(state.catalog.runs[0]);
} catch { fail("Published measurements are unavailable. No synthetic results are displayed."); }
