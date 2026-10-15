import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { closeSync, constants, fstatSync, mkdirSync, openSync, readFileSync, readSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

assert.equal(process.env.GITHUB_ACTIONS, 'true');
assert.equal(process.env.RUNNER_OS, 'Linux');
assert.equal(process.platform, 'linux');
assert.equal(process.env.GITHUB_EVENT_NAME, 'workflow_dispatch');
assert.equal(process.env.GITHUB_REF, 'refs/heads/main');
assert.equal(process.env.GITHUB_REPOSITORY?.toLowerCase(), 'managedcode/keyload');
assert.match(process.env.GITHUB_WORKFLOW_REF ?? '', /^managedcode\/KeyLoad\/\.github\/workflows\/build-and-tests\.yml@refs\/heads\/main$/i);
assert(!process.env.LD_PRELOAD && !process.env.LD_LIBRARY_PATH, 'No caller-supplied library binding.');
const root = resolve(process.env.GITHUB_WORKSPACE ?? '');
assert.notEqual(root, '/');
const evidence = join(root, 'TestResults', 'kernel-clock-native');
mkdirSync(evidence); // Original exclusive run, never overwrite another observation.
const abi = join(root, 'TestResults', 'kernel-clock-abi');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function bounded(path, maximum) {
    const fd = openSync(path, constants.O_RDONLY | constants.O_NOFOLLOW);
    try {
        const state = fstatSync(fd);
        assert(state.isFile() && state.size > 0 && state.size <= maximum, path);
        const bytes = Buffer.alloc(state.size);
        let offset = 0;
        while (offset < bytes.length) {
            const count = readSync(fd, bytes, offset, bytes.length - offset, null);
            assert(count > 0, path);
            offset += count;
        }
        assert.equal(readSync(fd, Buffer.alloc(1), 0, 1, null), 0, path);
        const after = fstatSync(fd);
        assert(after.dev === state.dev && after.ino === state.ino && after.size === state.size, path);
        return bytes;
    } finally { closeSync(fd); }
}
const candidate = JSON.parse(bounded(join(abi, 'admission-candidate.original.json'), 65536));
assert.equal(candidate.schema, 1);
assert.equal(candidate.state, 'ORIGINAL_LINUX_TWO_ABI_CANDIDATE_REQUIRES_INDEPENDENT_REVIEW');
const generatedPath = 'src/KeyLoad.Storage.IO/Features/ResourceExecution/Interop/KernelClockVerifiedAbi.Generated.cs';
assert.equal(candidate.generatedPath, generatedPath);
assert.equal(candidate.generatedSha256, hash(bounded(join(root, generatedPath), 65536)));
for (const value of [candidate.runtime, candidate.host]) {
    for (const [field, environment] of Object.entries({ repository: 'GITHUB_REPOSITORY', source: 'GITHUB_SHA',
        run: 'GITHUB_RUN_ID', attempt: 'GITHUB_RUN_ATTEMPT', job: 'GITHUB_JOB' })) {
        assert.equal(value.metadata[field], process.env[environment]);
    }
}
let sequence = 0;
function command(program, args, timeout = 60000) {
    const result = spawnSync(program, args, { cwd: root, encoding: 'utf8', timeout, maxBuffer: 1048576 });
    writeFileSync(join(evidence, `command-${++sequence}.original.json`), JSON.stringify({ program, args,
        status: result.status, signal: result.signal, error: result.error?.message ?? null,
        stdout: result.stdout, stderr: result.stderr }, null, 2) + '\n', { flag: 'wx' });
    assert.equal(result.status, 0, `Original native prerequisite command ${sequence} failed.`);
    return result.stdout;
}
function imageHash(path) {
    const fd = openSync(path, constants.O_RDONLY | constants.O_NOFOLLOW);
    try {
        const before = fstatSync(fd);
        assert(before.isFile() && before.size > 0 && before.size <= 16 * 1024 * 1024);
        const digest = createHash('sha256');
        const bytes = Buffer.alloc(16384);
        let total = 0;
        while (total < before.size) {
            const count = readSync(fd, bytes, 0, Math.min(bytes.length, before.size - total), null);
            assert(count > 0);
            total += count;
            digest.update(bytes.subarray(0, count));
        }
        assert.equal(readSync(fd, bytes, 0, 1, null), 0);
        const after = fstatSync(fd);
        assert(after.dev === before.dev && after.ino === before.ino && after.size === before.size);
        return digest.digest('hex');
    } finally { closeSync(fd); }
}
const sourcePath = 'scripts/Features/ResourceExecution/kernel-clock-read-only-verify.c';
const source = bounded(join(root, sourcePath), 32768);
const compile = ['-std=c11', '-Wall', '-Wextra', '-Werror', '-O2'];
const integer = value => { assert.match(value, /^-?[0-9]{1,20}$/); return BigInt(value); };
const billion = 1000000000n;
function nanos(seconds, fraction) {
    const sec = integer(seconds), part = integer(fraction);
    assert(sec >= 0n && part >= 0n && part < billion);
    return sec * billion + part;
}
function requireObservation(raw, expected) {
    assert(Buffer.byteLength(raw) <= 16384);
    const observed = JSON.parse(raw);
    assert.equal(observed.schema, 1);
    assert.equal(observed.gnuVersion, expected.probe.gnuVersion);
    assert.equal(observed.libcPath, expected.metadata.libcPath);
    assert.equal(observed.readOnlyModes, 0);
    assert.equal(observed.state, 0, 'Only actual TIME_OK is admitted; failures are retained.');
    assert.equal(observed.nativeError, 0);
    assert(Number.isSafeInteger(observed.status) && observed.status >= 0 && observed.status <= 65535);
    assert.equal(observed.status & (0x40 | 0x1000 | 0x10 | 0x20), 0);
    const maximum = integer(observed.maximumErrorMicroseconds), estimated = integer(observed.estimatedErrorMicroseconds);
    assert(maximum >= 0n && maximum <= 100000n && estimated >= 0n && estimated <= 100000n);
    const duration = nanos(observed.finishedSeconds, observed.finishedNanoseconds)
        - nanos(observed.startedSeconds, observed.startedNanoseconds);
    assert(duration >= 0n && duration <= 25000000n, 'Original single C observation must meet the acquisition tolerance.');
    const subsecond = integer(observed.nativeSubsecond), nanoMode = (observed.status & 0x2000) !== 0;
    assert(subsecond >= 0n && subsecond < (nanoMode ? billion : 1000000n));
    const native = integer(observed.nativeSeconds) * billion + subsecond * (nanoMode ? 1n : 1000n);
    const real = nanos(observed.realtimeSeconds, observed.realtimeNanoseconds);
    const difference = native > real ? native - real : real - native;
    assert(difference <= duration + (maximum > estimated ? maximum : estimated) * 1000n + (nanoMode ? 1n : 1000n));
    return observed;
}
const image = `keyload-clock-native:${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}`;
const containers = new Set(), failures = [];
let primary, hostObservation, runtimeObservation, nativeImageId;
let imageOwned = false;
const sourcePaths = ['.github/workflows/build-and-tests.yml', 'Dockerfile', 'global.json',
    'scripts/Features/ResourceExecution/kernel-clock-abi-probe.c',
    'scripts/Features/ResourceExecution/kernel-clock-abi-produce.mjs',
    'scripts/Features/ResourceExecution/kernel-clock-abi-generate.mjs',
    'scripts/Features/ResourceExecution/kernel-clock-abi-tooling.Dockerfile', sourcePath,
    'scripts/Features/ResourceExecution/kernel-clock-native-verify.mjs'];
const sourceHashes = Object.fromEntries(sourcePaths.map(path => [path, hash(bounded(join(root, path), 131072))]));
writeFileSync(join(evidence, 'source.original.json'), JSON.stringify({ schema: 1,
    source: process.env.GITHUB_SHA, run: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT,
    job: process.env.GITHUB_JOB, workflowRef: process.env.GITHUB_WORKFLOW_REF, sourceHashes }, null, 2) + '\n', { flag: 'wx' });
try {
    assert.equal(imageHash(candidate.host.metadata.libcPath), candidate.host.metadata.libcSha256After);
    assert.equal(command('dpkg-query', ['-W', '-f=${Version}', 'libc6-dev']).trim(), candidate.host.metadata.headerPackageVersion);
    assert.equal(hash(command('gcc', ['--version'])), candidate.host.metadata.compilerIdentitySha256);
    const binary = join(evidence, 'host-native.original.bin');
    command('gcc', [...compile, sourcePath, '-ldl', '-o', binary]);
    const hostRaw = command(binary, []);
    writeFileSync(join(evidence, 'host-observation.original.json'), hostRaw, { flag: 'wx' });
    hostObservation = requireObservation(hostRaw, candidate.host);
    assert.equal(imageHash(candidate.host.metadata.libcPath), candidate.host.metadata.libcSha256After);
    assert.equal(command('dpkg-query', ['-W', '-f=${Version}', 'libc6']).trim(), candidate.host.metadata.libcVersionAfter);
    assert.equal(command('dpkg-query', ['-W', '-f=${Version}', 'libc6-dev']).trim(), candidate.host.metadata.headerPackageVersion);
    imageOwned = true;
    command('docker', ['build', '--pull', '--progress=plain', '-f',
        'scripts/Features/ResourceExecution/kernel-clock-abi-tooling.Dockerfile', '--build-arg',
        `KEYLOAD_RUNTIME_BASE=${candidate.runtime.metadata.baseImage}`, '-t', image, '.'], 600000);
    const inspected = JSON.parse(command('docker', ['image', 'inspect', image]));
    assert(Array.isArray(inspected) && inspected.length === 1);
    nativeImageId = inspected[0].Id;
    const users = inspected[0].Config.Env.filter(value => /^APP_UID=[1-9][0-9]{0,8}$/.test(value));
    assert.equal(users.length, 1, 'Use the actual pinned runtime non-root identity; no fallback UID.');
    const run = shell => {
        const name = `keyload-clock-native-${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}-${sequence}`;
        containers.add(name);
        const result = command('docker', ['run', '--name', name, '--rm', '--network=none', '--read-only',
            '--user', users[0].slice('APP_UID='.length), '--cap-drop=ALL', '--security-opt=no-new-privileges',
            '--tmpfs', '/tmp:rw,exec,nosuid,size=1048576', '--mount',
            `type=bind,src=${join(root, sourcePath)},dst=/native-clock-verify.c,readonly`, '--entrypoint', '/bin/sh', image,
            '-eu', '-c', shell]);
        containers.delete(name);
        return result;
    };
    const expectedIdentity = [candidate.runtime.metadata.libcSha256After,
        candidate.runtime.metadata.libcVersionAfter, candidate.runtime.metadata.headerPackageVersion];
    const identityCommand = 'cat /libc-sha256.after /libc-version.after /libc-dev-version.original';
    assert.deepEqual(run(identityCommand).trim().split('\n'), expectedIdentity);
    const runtimeCompiler = run('gcc --version');
    writeFileSync(join(evidence, 'runtime-compiler.original.txt'), runtimeCompiler, { flag: 'wx' });
    const runtimeRaw = run('gcc -std=c11 -Wall -Wextra -Werror -O2 /native-clock-verify.c -ldl -o /tmp/native-clock-verify; sha256sum /tmp/native-clock-verify >&2; exec /tmp/native-clock-verify');
    const runtimeCommand = `command-${sequence}.original.json`;
    const runtimeExecution = JSON.parse(bounded(join(evidence, runtimeCommand), 1048576));
    const runtimeBinary = /^([0-9a-f]{64})  \/tmp\/native-clock-verify\n$/.exec(runtimeExecution.stderr);
    assert(runtimeBinary, 'Retain the exact actual compiled runtime verifier identity.');
    assert.deepEqual(run(identityCommand).trim().split('\n'), expectedIdentity);
    writeFileSync(join(evidence, 'runtime-observation.original.json'), runtimeRaw, { flag: 'wx' });
    runtimeObservation = requireObservation(runtimeRaw, candidate.runtime);
    writeFileSync(join(evidence, 'verification.original.json'), JSON.stringify({ schema: 1,
        source: process.env.GITHUB_SHA, run: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT,
        job: process.env.GITHUB_JOB, workflowRef: process.env.GITHUB_WORKFLOW_REF,
        verifierSourceSha256: hash(source), hostVerifierBinarySha256: hash(bounded(binary, 1048576)),
        abiCandidateSha256: hash(bounded(join(abi, 'admission-candidate.original.json'), 65536)),
        generatedPredicateSha256: candidate.generatedSha256, nativeImageId,
        runtimeCompilerIdentitySha256: hash(runtimeCompiler), runtimeVerifierBinarySha256: runtimeBinary[1],
        runtimeCommandReceipt: runtimeCommand, sourceHashes,
        hostObservation, runtimeObservation,
        claims: { originalCReadOnlyObservation: true, managedObserverExecuted: false,
            providerQualified: false, operationAdmissionQualified: false, physicalAccuracy: false, performance: false }
    }, null, 2) + '\n', { flag: 'wx' });
} catch (error) { primary = error; }
finally {
    for (const container of containers) {
        try { command('docker', ['rm', '--force', container]); }
        catch (error) { failures.push(error); }
    }
    if (imageOwned) {
        try { command('docker', ['image', 'rm', image]); }
        catch (error) { failures.push(error); }
    }
    try {
        for (const path of sourcePaths) assert.equal(hash(bounded(join(root, path), 131072)), sourceHashes[path], path);
    } catch (error) { failures.push(error); }
}
if (primary || failures.length) {
    throw new AggregateError([...(primary ? [primary] : []), ...failures],
        'Original native compatibility or owned cleanup failed; retain every command receipt.');
}
writeFileSync(join(evidence, 'completion.original.json'), JSON.stringify({ schema: 1,
    source: process.env.GITHUB_SHA, run: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT,
    job: process.env.GITHUB_JOB, allOriginalCommandsSucceeded: true, ownedCleanupCompleted: true,
    sourceUnchanged: true, managedProviderQualified: false, physicalAccuracy: false
}, null, 2) + '\n', { flag: 'wx' });
