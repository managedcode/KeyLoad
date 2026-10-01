import { readFile, readdir, mkdir, copyFile, writeFile } from "node:fs/promises";
import { resolve, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const args = Object.fromEntries(process.argv.slice(2).map(argument => { const i = argument.indexOf("="); if (i < 0) throw new Error("Arguments require --name=value"); return [argument.slice(0, i), argument.slice(i + 1)]; }));
const source = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const reports = resolve(args["--reports"] ?? "artifacts/comparisons");
const output = resolve(args["--output"] ?? "_site");
const expectedRevision = args["--revision"];
if (output === source || reports === output || output === resolve("/")) throw new Error("The output must be an isolated generated directory");
await mkdir(join(output, "data", "runs"), { recursive: true });
for (const asset of ["index.html", "styles.css", "app.js", "measurements.mjs", "favicon.svg"]) await copyFile(join(source, asset), join(output, asset));
await writeFile(join(output, ".nojekyll"), "");
await writeFile(join(output, "CNAME"), "www.keyload.cloud\n");
await writeFile(join(output, "robots.txt"), "User-agent: *\nAllow: /\nSitemap: https://www.keyload.cloud/sitemap.xml\n");
await writeFile(join(output, "sitemap.xml"), '<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>https://www.keyload.cloud/</loc></url></urlset>\n');
const runs = [];
for (const folder of (await readdir(reports, { withFileTypes: true })).filter(item => item.isDirectory()).sort((a, b) => a.name.localeCompare(b.name))) {
  if (!/^[a-z0-9-]+$/.test(folder.name)) throw new Error("Invalid report directory name");
  const root = join(reports, folder.name);
  const report = JSON.parse(await readFile(join(root, "results.json"), "utf8"));
  const names = new Set(report.targets?.map(target => target.name));
  if (report.schemaVersion !== 2 || names.size !== 6 || !["KeyLoad", "PostgreSQL + pgvector", "Qdrant", "RabbitMQ", "Redis", "Neo4j"].every(name => names.has(name))) throw new Error("A complete six-engine schema-2 report is required");
  if (expectedRevision && report.sourceRevision !== expectedRevision) throw new Error("Report source revision differs from the CI revision");
  if (report.cases.length !== 36 * report.options.repetitions || report.cases.some(item => item.status === "failed")) throw new Error("Failed or incomplete comparisons cannot be published");
  const measured = report.cases.filter(item => item.status === "measured");
  if (measured.length !== 20 * report.options.repetitions || measured.some(item => item.measurement?.attempts !== report.options.operations || item.measurement?.successes !== report.options.operations || !item.measurement.clientResources)) throw new Error("Every supported attempt must be verified and include resource measurements");
  const target = join(output, "data", "runs", folder.name); await mkdir(target, { recursive: true });
  for (const file of ["results.json", "results.md", "samples.csv"]) await copyFile(join(root, file), join(target, file));
  const o = report.options;
  runs.push({ id: folder.name, label: `${o.payloadBytes / 1024} KiB · ${o.concurrency} clients · ${o.documents} documents · ${o.graphDepth} graph hops${folder.name === "smoke" ? " · correctness smoke" : ""}`,
    report: `runs/${folder.name}/results.json`, evidenceUrl: args["--evidence-url"] ?? "https://github.com/managedcode/KeyLoad/actions", startedAt: report.startedAt });
}
if (!runs.length) throw new Error("At least one verified run is required");
runs.sort((a, b) => Number(a.id === "smoke") - Number(b.id === "smoke") || a.id.localeCompare(b.id));
await writeFile(join(output, "data", "catalog.json"), JSON.stringify({ generatedAt: new Date().toISOString(), runs }, null, 2) + "\n");
console.log(`Built ${runs.length} verified workload profiles in ${output}`);
