import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, realpathSync, statSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

assert.equal(process.env.GITHUB_ACTIONS, 'true', 'Only the original trusted Linux producer runs this program.');
assert.equal(process.env.RUNNER_OS, 'Linux');
const root = resolve(process.env.GITHUB_WORKSPACE ?? '');
assert.notEqual(root, '/');
const evidence = join(root, 'TestResults', 'kernel-clock-abi');
mkdirSync(join(root, 'TestResults'), { recursive: true });
mkdirSync(evidence); // Never overwrite another producer's original receipts.
let commandSequence = 0;
const originalCommand = (command, args, timeout = 60000, binary = false) => {
    const result = spawnSync(command, args, { cwd: root, encoding: binary ? null : 'utf8', timeout, maxBuffer: 1048576 });
    const receipt = { command, args, status: result.status, signal: result.signal,
        error: result.error?.message ?? null,
        stdout: binary ? { bytes: result.stdout?.length ?? 0, sha256: hash(result.stdout ?? Buffer.alloc(0)) } : result.stdout,
        stderr: binary ? result.stderr?.toString('utf8') : result.stderr };
    writeFileSync(join(evidence, `command-${++commandSequence}.original.json`), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
    assert.equal(result.status, 0, `Original ${command} failed; retain its receipt.`);
    return result.stdout;
};
const docker = (args, binary = false) => originalCommand('docker', args, 600000, binary);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const from = readFileSync(join(root, 'Dockerfile'), 'utf8').split('\n')
    .find(line => /^FROM mcr\.microsoft\.com\/dotnet\/aspnet:.* AS runtime$/.test(line));
assert(from, 'Canonical digest-pinned runtime base is required.');
const baseImage = from.split(' ')[1];
assert.match(baseImage, /@sha256:[0-9a-f]{64}$/);
const image = `keyload-clock-abi:${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}`;
const ownedContainers = new Set();
const cleanupFailures = [];
let primaryFailure;
try {
    const build = docker(['build', '--pull', '--progress=plain', '-f', 'scripts/Features/ResourceExecution/kernel-clock-abi-tooling.Dockerfile',
        '--build-arg', `KEYLOAD_RUNTIME_BASE=${baseImage}`, '-t', image, '.']);
    writeFileSync(join(evidence, 'tooling-build.original.log'), build, { flag: 'wx' });
    const inspect = docker(['image', 'inspect', image]);
    writeFileSync(join(evidence, 'tooling-image.original.json'), inspect, { flag: 'wx' });
    const run = (command, binary = false) => {
        const name = `keyload-clock-${process.env.GITHUB_RUN_ID}-${commandSequence}`;
        ownedContainers.add(name);
        const result = docker(['run', '--name', name, '--rm', '--network=none', '--read-only', '--entrypoint', '/bin/sh', image, '-eu', '-c', command], binary);
        ownedContainers.delete(name);
        return result;
    };
    const raw = run('/clock-abi-probe');
    writeFileSync(join(evidence, 'probe.original.json'), raw, { flag: 'wx' });
    const probe = JSON.parse(raw);
    const binary = run('cat /clock-abi-probe', true);
    writeFileSync(join(evidence, 'probe.original.bin'), binary, { flag: 'wx' });
    const runtimeCompiler = run('gcc --version');
    writeFileSync(join(evidence, 'compiler.original.txt'), runtimeCompiler, { flag: 'wx' });
    const metadata = { schema: 1, repository: process.env.GITHUB_REPOSITORY, source: process.env.GITHUB_SHA,
        run: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT, job: process.env.GITHUB_JOB,
        compilerIdentitySha256: hash(runtimeCompiler),
        baseImage, toolingImageId: JSON.parse(inspect)[0].Id,
        libcPath: probe.libcPath, libcBytes: probe.libcBytes,
        libcSha256Before: run('cat /libc-sha256.before').trim(), libcSha256After: run('cat /libc-sha256.after').trim(),
        headerPackageVersion: run('cat /libc-dev-version.original').trim(),
        libcVersionBefore: run('cat /libc-version.before').trim(), libcVersionAfter: run('cat /libc-version.after').trim(),
        gnuVersionBefore: run('cat /gnu-version.before').trim(), gnuVersionAfter: run('cat /gnu-version.after').trim(),
        probeSourceSha256: hash(readFileSync(join(root, 'scripts/Features/ResourceExecution/kernel-clock-abi-probe.c'))),
        probeBinarySha256: hash(binary), toolingDockerfileSha256: hash(readFileSync(join(root, 'scripts/Features/ResourceExecution/kernel-clock-abi-tooling.Dockerfile'))) };
    assert.equal(probe.gnuVersion, metadata.gnuVersionBefore);
    assert.equal(probe.gnuVersion, metadata.gnuVersionAfter);
    writeFileSync(join(evidence, 'producer.original.json'), JSON.stringify(metadata, null, 2) + '\n', { flag: 'wx' });
    
    // The host tuple is independently observed; it is not inferred from the container's GNU version.
    const native = (command, args) => originalCommand(command, args);
    const hostLibc = realpathSync(native('gcc', ['-print-file-name=libc.so.6']).trim());
    const hostState = statSync(hostLibc);
    assert(hostState.isFile() && hostState.size > 0 && hostState.size <= 16 * 1024 * 1024);
    const hostBefore = { sha: hash(readFileSync(hostLibc)), version: native('dpkg-query', ['-W', '-f=${Version}', 'libc6']).trim(),
        gnu: native('getconf', ['GNU_LIBC_VERSION']).trim().split(' ')[1] };
    const hostHeaderVersion = native('dpkg-query', ['-W', '-f=${Version}', 'libc6-dev']).trim();
    assert.equal(hostHeaderVersion, hostBefore.version, 'Host dev headers must match the unchanged runtime package.');
    const hostBinaryPath = join(evidence, 'host-probe.original.bin');
    const hostCompiler = native('gcc', ['--version']);
    writeFileSync(join(evidence, 'host-compiler.original.txt'), hostCompiler, { flag: 'wx' });
    native('gcc', ['-std=c11', '-Wall', '-Wextra', '-Werror', '-O2',
        'scripts/Features/ResourceExecution/kernel-clock-abi-probe.c', '-ldl', '-o', hostBinaryPath]);
    const hostRaw = native(hostBinaryPath, []);
    writeFileSync(join(evidence, 'host-probe.original.json'), hostRaw, { flag: 'wx' });
    const hostProbe = JSON.parse(hostRaw);
    assert.equal(hostProbe.libcPath, hostLibc);
    const hostMetadata = { schema: 1, repository: process.env.GITHUB_REPOSITORY, source: process.env.GITHUB_SHA,
        run: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT, job: process.env.GITHUB_JOB,
        runnerImageVersion: process.env.ImageVersion, runnerImageOS: process.env.ImageOS,
        libcPath: hostLibc, libcBytes: hostState.size, libcSha256Before: hostBefore.sha, libcSha256After: hash(readFileSync(hostLibc)),
        headerPackageVersion: hostHeaderVersion,
        libcVersionBefore: hostBefore.version, libcVersionAfter: native('dpkg-query', ['-W', '-f=${Version}', 'libc6']).trim(),
        gnuVersionBefore: hostBefore.gnu, gnuVersionAfter: native('getconf', ['GNU_LIBC_VERSION']).trim().split(' ')[1],
        probeSourceSha256: metadata.probeSourceSha256, probeBinarySha256: hash(readFileSync(hostBinaryPath)),
        compilerIdentitySha256: hash(hostCompiler) };
    assert.equal(hostMetadata.libcSha256Before, hostMetadata.libcSha256After);
    assert.equal(hostMetadata.libcVersionBefore, hostMetadata.libcVersionAfter);
    assert.equal(hostHeaderVersion, native('dpkg-query', ['-W', '-f=${Version}', 'libc6-dev']).trim());
    assert.equal(hostMetadata.gnuVersionBefore, hostMetadata.gnuVersionAfter);
    writeFileSync(join(evidence, 'host-producer.original.json'), JSON.stringify(hostMetadata, null, 2) + '\n', { flag: 'wx' });
} catch (error) {
    primaryFailure = error;
} finally {
    for (const container of ownedContainers) {
        try { docker(['rm', '--force', container]); }
        catch (error) { cleanupFailures.push(error); }
    }
    try { docker(['image', 'rm', image]); }
    catch (error) { cleanupFailures.push(error); }
}
if (primaryFailure || cleanupFailures.length) {
    throw new AggregateError([...(primaryFailure ? [primaryFailure] : []), ...cleanupFailures],
        'Native ABI production or owned resource cleanup failed; retain every original command receipt.');
}
