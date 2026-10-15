import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { constants, openSync, fstatSync, readFileSync, readSync, closeSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

assert.equal(process.env.GITHUB_ACTIONS, 'true', 'Original trusted Linux Actions producer is required.');
assert.equal(process.env.RUNNER_OS, 'Linux');
const root = resolve(process.env.GITHUB_WORKSPACE ?? '');
assert.notEqual(root, '/');
const evidence = join(root, 'TestResults', 'kernel-clock-abi');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function bounded(name, maximum) {
    const fd = openSync(join(evidence, name), constants.O_RDONLY | constants.O_NOFOLLOW);
    try {
        const state = fstatSync(fd);
        assert(state.isFile() && state.size > 0 && state.size <= maximum, name);
        const bytes = Buffer.alloc(state.size);
        let offset = 0;
        while (offset < bytes.length) {
            const count = readSync(fd, bytes, offset, bytes.length - offset, null);
            assert(count > 0, name);
            offset += count;
        }
        assert.equal(readSync(fd, Buffer.alloc(1), 0, 1, null), 0, name);
        const after = fstatSync(fd);
        assert(after.isFile() && after.dev === state.dev && after.ino === state.ino && after.size === state.size, name);
        return bytes;
    } finally { closeSync(fd); }
}
function verify(prefix) {
    const raw = bounded(`${prefix}probe.original.json`, 16384);
    const probe = JSON.parse(raw);
    const metadata = JSON.parse(bounded(`${prefix}producer.original.json`, 32768));
    assert.equal(probe.schema, 1);
    assert.equal(metadata.schema, 1);
    for (const [field, environment] of Object.entries({ repository: 'GITHUB_REPOSITORY', source: 'GITHUB_SHA',
        run: 'GITHUB_RUN_ID', attempt: 'GITHUB_RUN_ATTEMPT', job: 'GITHUB_JOB' })) {
        assert.equal(metadata[field], process.env[environment]);
    }
    assert.match(metadata.source, /^[0-9a-f]{40}$/);
    assert.match(metadata.repository, /^managedcode\/KeyLoad$/i);
    for (const field of ['libcSha256Before', 'libcSha256After', 'probeSourceSha256', 'probeBinarySha256']) {
        assert.match(metadata[field], /^[0-9a-f]{64}$/);
    }
    assert.equal(metadata.libcSha256Before, metadata.libcSha256After);
    assert.equal(metadata.libcVersionBefore, metadata.libcVersionAfter);
    assert.equal(metadata.headerPackageVersion, metadata.libcVersionBefore);
    assert.equal(metadata.gnuVersionBefore, metadata.gnuVersionAfter);
    assert.equal(metadata.gnuVersionAfter, probe.gnuVersion);
    assert.match(probe.gnuVersion, /^[0-9]+\.[0-9]+(?:\.[0-9]+)?$/);
    assert.equal(`${probe.headerGnuMajor}.${probe.headerGnuMinor}`, probe.gnuVersion.split('.').slice(0, 2).join('.'));
    assert.equal(metadata.probeSourceSha256, hash(readFileSync(join(root, 'scripts/Features/ResourceExecution/kernel-clock-abi-probe.c'))));
    assert.equal(metadata.probeBinarySha256, hash(bounded(`${prefix}probe.original.bin`, 1048576)));
    assert.match(metadata.compilerIdentitySha256, /^[0-9a-f]{64}$/);
    assert.equal(metadata.compilerIdentitySha256, hash(bounded(`${prefix}compiler.original.txt`, 16384)));
    assert.deepEqual(probe.timex, { size: 208, modes: 0, maximumError: 24, estimatedError: 32, status: 40, seconds: 72, subsecond: 80, tai: 160 });
    assert.deepEqual(probe.dlInfo, { size: 32, fileName: 0, base: 8, symbolName: 16, symbolAddress: 24 });
    assert.equal(probe.pointerBytes, 8);
    assert.equal(probe.longBytes, 8);
    assert.equal(probe.littleEndian, true);
    assert(Number.isSafeInteger(probe.libcBytes) && probe.libcBytes > 0 && probe.libcBytes <= 16 * 1024 * 1024);
    assert.match(probe.libcDevice, /^[0-9]{1,20}$/);
    assert.match(probe.libcInode, /^[0-9]{1,20}$/);
    assert.equal(probe.libcPath, metadata.libcPath);
    assert.equal(probe.libcBytes, metadata.libcBytes);
    assert.match(probe.libcPath, /^\/[A-Za-z0-9_./+-]+$/);
    const architecture = { x86_64: 'X64', aarch64: 'Arm64' }[probe.architecture];
    assert(architecture && architecture.toLowerCase() === String(process.env.RUNNER_ARCH).toLowerCase());
    return { probe, metadata, architecture, rawProbeSha256: hash(raw) };
}
const runtime = verify('');
const host = verify('host-');
assert.equal(runtime.architecture, host.architecture);
assert.equal(runtime.metadata.toolingDockerfileSha256,
    hash(readFileSync(join(root, 'scripts/Features/ResourceExecution/kernel-clock-abi-tooling.Dockerfile'))));
const imageReceipt = JSON.parse(bounded('tooling-image.original.json', 131072));
assert(Array.isArray(imageReceipt) && imageReceipt.length === 1);
assert.match(runtime.metadata.toolingImageId, /^sha256:[0-9a-f]{64}$/);
assert.equal(imageReceipt[0].Id, runtime.metadata.toolingImageId);
assert.match(runtime.metadata.baseImage, /^mcr\.microsoft\.com\/dotnet\/aspnet:10\.[0-9.]+@sha256:[0-9a-f]{64}$/);
const runtimeLine = readFileSync(join(root, 'Dockerfile'), 'utf8').split('\n')
    .find(line => /^FROM mcr\.microsoft\.com\/dotnet\/aspnet:.* AS runtime$/.test(line));
assert.equal(runtimeLine, `FROM ${runtime.metadata.baseImage} AS runtime`);
assert.match(host.metadata.runnerImageVersion, /^[A-Za-z0-9_.-]{1,128}$/);
assert.match(host.metadata.runnerImageOS, /^[A-Za-z0-9_.-]{1,128}$/);
const tuple = value => `runtime == new KernelClockRuntime(${JSON.stringify(value.probe.gnuVersion)}, Architecture.${value.architecture}, true, VerifiedPointerBytes)\n                && string.Equals(imageSha256, ${JSON.stringify(value.metadata.libcSha256After)}, StringComparison.Ordinal)`;
const generated = `using System.Runtime.InteropServices;\n\nnamespace KeyLoad.Storage.IO;\n\n// Original Linux producer ${runtime.metadata.run}/${runtime.metadata.attempt}; runtime ${runtime.rawProbeSha256}; host ${host.rawProbeSha256}.\ninternal static partial class KernelClockVerifiedAbi\n{\n    private const int VerifiedPointerBytes = 8;\n\n    internal static partial bool Matches(KernelClockRuntime runtime, string imageSha256)\n        => (${tuple(runtime)})\n            || (${tuple(host)});\n}\n`;
const target = join(root, 'src/KeyLoad.Storage.IO/Features/ResourceExecution/Interop/KernelClockVerifiedAbi.Generated.cs');
mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, generated, { flag: 'wx' });
const receipt = { schema: 1, state: 'ORIGINAL_LINUX_TWO_ABI_CANDIDATE_REQUIRES_INDEPENDENT_REVIEW', runtime, host,
    generatedSha256: hash(generated), generatedPath: target.slice(root.length + 1),
    claims: { kernelObservationExecuted: false, physicalAccuracy: false, providerHealthy: false, qualified: false } };
writeFileSync(join(evidence, 'admission-candidate.original.json'), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
