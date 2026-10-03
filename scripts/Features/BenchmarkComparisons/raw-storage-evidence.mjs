import { createReadStream } from 'node:fs';
import { mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { arch, cpus, homedir, platform, totalmem } from 'node:os';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { validateReport } from './raw-storage-report.mjs';

const garnetDigest = 'cc11bd69f27417f09c42e2927598efd834b4d03919ba0eb29ce6e1d3251b7230';
const packageVersions = { 'Microsoft.Garnet': '2.2.0', ZoneTree: '1.9.8', BenchmarkDotNet: '0.15.8' };
const metadataName = 'execution.json';

function requireValue(condition, message) {
    if (!condition) throw new Error(message);
}

function executor(engine) {
    const env = process.env;
    requireValue(engine === 'zonetree' || engine === 'tsavorite', 'Unsupported raw-storage engine.');
    requireValue(platform() === 'linux', 'Raw-storage measurements require Linux.');
    requireValue(env.GITHUB_ACTIONS === 'true' && env.GITHUB_REPOSITORY === 'managedcode/KeyLoad'
        && env.GITHUB_REF === 'refs/heads/main' && env.GITHUB_JOB === 'raw-storage', 'Wrong raw-storage executor.');
    requireValue(env.KEYLOAD_RAW_STORAGE_ENGINE === engine, 'Engine selection does not match the executor.');
    requireValue(/^[0-9a-f]{40}$/.test(env.GITHUB_SHA ?? ''), 'Missing exact source revision.');
    requireValue(/^[1-9][0-9]*$/.test(env.GITHUB_RUN_ID ?? '')
        && /^[1-9][0-9]*$/.test(env.GITHUB_RUN_ATTEMPT ?? ''), 'Missing actual GitHub run identity.');
    return { source: env.GITHUB_SHA, runId: env.GITHUB_RUN_ID, attempt: env.GITHUB_RUN_ATTEMPT,
        job: env.GITHUB_JOB, engine };
}

async function digest(path) {
    const hash = createHash('sha256');
    for await (const chunk of createReadStream(path)) hash.update(chunk);
    return hash.digest('hex');
}

async function packages() {
    const root = process.env.NUGET_PACKAGES || join(homedir(), '.nuget', 'packages');
    const facts = {};
    for (const [name, version] of Object.entries(packageVersions)) {
        const id = name.toLowerCase();
        const sha256 = await digest(join(root, id, version, `${id}.${version}.nupkg`));
        facts[name] = { version, sha256 };
    }
    requireValue(facts['Microsoft.Garnet'].sha256 === garnetDigest, 'Garnet package bytes differ from the approved feed pin.');
    return facts;
}

function declaredProfile() {
    return { mode: 'resident-cache', durability: 'none', workerCount: 1, recordCount: 4096,
        payloadBytes: [32, 1024], keyBytes: 16, seed: 1729, maximumWrites: 65536,
        workload: ['PointRead', 'MissingRead', 'Overwrite', 'CreateDelete'], hotRecord: 0,
        launchCount: 1, warmupCount: 3, iterationCount: 5, invocationCount: 1024, unrollFactor: 1,
        createDeleteOperationsPerInvoke: 2,
        declaredSettings: {
            tsavorite: { indexBytes: 4 * 1024 * 1024, pageBytes: 1024 * 1024,
                logBytes: 128 * 1024 * 1024, segmentBytes: 128 * 1024 * 1024,
                mutableFraction: 0.9, readCacheEnabled: false, tryRecoverLatest: false, logDevice: 'NullDevice' },
            zonetree: { writeAheadLog: 'None', deletedValue: 'empty', maintenance: 'native defaults' }
        },
        limits: ['Raw engines only; no Garnet RESP/AOF or KeyLoad database qualification.',
            'One worker and hot seeded record; no concurrency, cluster, random-keyspace or durability claim.',
            'BDN allocation statistics do not establish native RSS, CPU or request percentiles.'] };
}

async function prepare(directory, identity) {
    await mkdir(directory, { recursive: true });
    const metadata = { schema: 'keyload.raw-storage.v1', executor: identity, packages: await packages(),
        profile: declaredProfile(), capturedAt: new Date().toISOString(),
        host: { platform: platform(), architecture: arch(), logicalCpuCount: cpus().length,
            processorModels: [...new Set(cpus().map(cpu => cpu.model))], totalMemoryBytes: totalmem() } };
    await writeFile(join(directory, metadataName), `${JSON.stringify(metadata, null, 2)}\n`, { flag: 'wx' });
}

async function originalFile(path) {
    const info = await stat(path);
    requireValue(info.isFile() && info.size > 0, 'Missing original raw-storage output.');
    return { name: path.split('/').at(-1), bytes: info.size, sha256: await digest(path) };
}

async function verify(directory, identity) {
    const metadata = JSON.parse(await readFile(join(directory, metadataName), 'utf8'));
    requireValue(metadata.schema === 'keyload.raw-storage.v1'
        && JSON.stringify(metadata.executor) === JSON.stringify(identity), 'Raw-storage execution identity changed.');
    requireValue(JSON.stringify(metadata.packages) === JSON.stringify(await packages()), 'Executed package bytes changed.');
    requireValue(JSON.stringify(metadata.profile) === JSON.stringify(declaredProfile()), 'Declared raw-storage profile changed.');
    const results = join(directory, 'results');
    const files = await readdir(results, { withFileTypes: true });
    requireValue(files.every(file => file.isFile()), 'Unexpected raw-storage report entry.');
    const full = files.filter(file => file.name.endsWith('-report-full.json'));
    const csv = files.filter(file => file.name.endsWith('-report.csv'));
    requireValue(full.length === 1 && csv.length === 1, 'Missing or ambiguous original full JSON/CSV.');
    const reportPath = join(results, full[0].name);
    requireValue((await stat(reportPath)).size <= 8 * 1024 * 1024, 'Raw-storage report exceeds its bounded input.');
    const report = JSON.parse(await readFile(reportPath, 'utf8'));
    validateReport(report, identity.engine);
    const originals = await Promise.all([reportPath, join(results, csv[0].name), join(directory, 'stdout.txt')]
        .map(originalFile));
    const receipt = { schema: 'keyload.raw-storage.complete.v1', executor: identity, packages: metadata.packages,
        profile: metadata.profile, originalFiles: originals, cellCount: 8, rawActualRowsPerCell: 5,
        RuntimeVersion: report.HostEnvironmentInfo.RuntimeVersion, hostEnvironment: report.HostEnvironmentInfo,
        qualification: 'Complete raw-cache report shape; external GitHub job/artifact authentication is required.' };
    await writeFile(join(directory, 'complete.json'), `${JSON.stringify(receipt, null, 2)}\n`, { flag: 'wx' });
}

async function main() {
    const { values } = parseArgs({ options: {
        mode: { type: 'string' }, engine: { type: 'string' }, directory: { type: 'string' }
    }, strict: true, allowPositionals: false });
    requireValue(values.mode === 'prepare' || values.mode === 'verify', 'Unsupported evidence mode.');
    const identity = executor(values.engine);
    const directory = resolve(values.directory ?? '');
    requireValue(directory === resolve('artifacts', 'raw-storage', identity.engine), 'Wrong owned raw-storage output path.');
    if (values.mode === 'prepare') await prepare(directory, identity);
    else await verify(directory, identity);
    process.stdout.write(`Raw-storage evidence ${values.mode} completed for ${identity.engine}.\n`);
}

main().catch(error => {
    process.stderr.write(`${error instanceof Error ? error.message : 'Invalid raw-storage evidence.'}\n`);
    process.exitCode = 1;
});
