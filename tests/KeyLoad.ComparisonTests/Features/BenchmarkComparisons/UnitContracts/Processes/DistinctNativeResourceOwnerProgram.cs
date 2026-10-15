namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class DistinctNativeResourceOwnerProgram
{
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { createHash } from 'node:crypto';
        import { mkdir, readFile, readdir, writeFile, access } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const root = process.argv[1];
        const moduleRoot = path.dirname(process.argv[2]);
        const load = name => import(pathToFileURL(path.join(moduleRoot, name)).href);
        const resource = await load('server-resource-evidence.mjs');
        const aggregate = await load('aggregate-evidence.mjs');
        const planner = await load('isolated-plan.mjs');
        const scale = await load('scaled-isolated-plan.mjs');
        const { AGGREGATE } = await load('aggregate-contracts.mjs');
        const { isolatedEvidenceJobName } = await load('isolated-github-contract.mjs');
        const profileIndex = Number(process.argv[3]);
        const duplicateIndex = Number(process.argv[4]);
        assert.ok(profileIndex === 0 || profileIndex === 1);
        assert.ok(duplicateIndex === 1 || duplicateIndex === 2);
        const contract = planner.readIsolatedContract();
        const plan = scale.createScaledPlans(contract)[profileIndex];
        assert.equal(plan.profileSettings.documents, profileIndex === 0 ? 100000 : 1000000);
        assert.equal(plan.profileSettings.operations, 100000);
        assert.deepEqual(contract.nodeCounts, [1, 3]);
        const cohort = { sourceRevision: 'a'.repeat(40), runId: 37500000001, attempt: 1,
          repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks', profile: plan.profile };
        const hash = bytes => createHash('sha256').update(bytes).digest('hex');
        const encode = value => Buffer.from(JSON.stringify(value));
        const input = path.join(root, 'input');
        const planFile = path.join(root, 'plan.json');
        const proofFile = path.join(root, 'proof.json');
        const originalOutput = path.join(root, 'original-aggregate');
        const rejectedOutput = path.join(root, 'rejected-aggregate');
        const repairedOutput = path.join(root, 'repaired-aggregate');
        const hardware = { kernel: 'Linux controlled functional fixture', architecture: 'X64', cpuVendor: 'GenuineIntel',
          cpuFamily: 6, cpuModel: 143, cpuStepping: 0, logicalCpuCount: 4, physicalCoreCount: 2,
          logicalCpuMembership: '0,1,2,3', physicalCoreMembership: ['0:0', '0:1'], memoryBytes: 8589934592 };
        const envelope = { cpuQuota: '4', cpuSet: '0-3', memoryLimitBytes: '8589934592' };
        const policy = { cadenceMilliseconds: 100, maximumObservationMilliseconds: 1000,
          cleanupThresholdMilliseconds: 1000, processSettlementMilliseconds: 100, maxProcesses: 16,
          maxMounts: 2, maxFileBytes: 512, minimumCommandBytes: 8, maxHardwareBytes: 1024,
          maxSampleMetadataBytes: 2048, maxSidecarBytes: 8192, maxWorkerBytes: 65536, maxSamples: 10,
          maxCgroupAncestors: 8, nativeReadBufferBytes: 128, maxNativeOutputBytes: 256, standardErrorOutputDivisor: 4 };
        const mount = { containerPath: '/data', mountType: 'bind', fileSystem: 'ext4',
          capacityBytes: 107374182400, availableBytes: 53687091200 };
        function container(cellIndex, nodeIndex)
        {
          return { resource: 'controlled-resource-' + nodeIndex,
            containerId: ((cellIndex + 1) * 4 + nodeIndex).toString(16).padStart(64, '0'),
            imageId: 'sha256:' + 'e'.repeat(64), startedAt: '2026-10-15T00:00:00Z', state: 'running',
            cpuLimit: '100000 100000', memoryLimitBytes: '1073741824', effectiveCpuQuota: '1',
            effectiveCpuSet: '0-3', effectiveMemoryLimitBytes: '1073741824', observedCpuUsageUsec: 1000,
            maxObservedCgroupMemoryBytes: 67108864, maxObservedRssBytes: 33554432, sampleCount: 2,
            writableMounts: [structuredClone(mount)] };
        }
        function sidecar(cell, cellIndex, workerHash, unsupported)
        {
          return { schema: 'server-resource-evidence.v2', sourceRevision: cohort.sourceRevision,
            workflowRunId: String(cohort.runId), runAttempt: String(cohort.attempt), jobId: String(cellIndex + 1),
            target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile,
            workerSha256: workerHash, hardware: structuredClone(hardware), appHostEnvelope: structuredClone(envelope),
            containers: unsupported ? [] : Array.from({ length: cell.nodeCount }, (_, node) => container(cellIndex, node)),
            missingEvidence: unsupported ? ['serverCpuRss'] : [], qualified: !unsupported,
            observationPolicy: structuredClone(policy) };
        }
        function expectedResource(value, sidecarHash, supported)
        {
          const comparison = supported ? { observationPolicy: policy, hardware, appHostEnvelope: envelope,
            containers: Array.from({ length: value.nodeCount }, () => ({ effectiveCpuQuota: '1', effectiveCpuSet: '0-3',
              effectiveMemoryLimitBytes: '1073741824', storage: [{ fileSystem: 'ext4', capacityBytes: 107374182400 }] })) } : null;
          return { sha256: sidecarHash, qualified: supported, missingEvidence: supported ? [] : ['serverCpuRss'],
            comparison, observedServerMemory: supported ? { bytes: value.nodeCount * 33554432, sampleCount: 2 } : null };
        }
        await mkdir(input, { recursive: true });
        await writeFile(planFile, encode(plan));
        const proof = { schemaVersion: 1, cohort, cells: [] };
        const originalCells = new Map();
        for (let index = 0; index < plan.cells.length; index++)
        {
          const cell = plan.cells[index];
          const unsupported = contract.unsupportedTopologies.some(item => item.target === cell.target && item.nodeCounts.includes(cell.nodeCount));
          const worker = { schemaVersion: contract.workerSchemaVersion,
            worker: { target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario, ...cohort, jobId: index + 1 },
            disposition: 'failed', reason: AGGREGATE.failureReason, report: null };
          const bytes = encode(worker);
          const workerHash = hash(bytes);
          const value = sidecar(cell, index, workerHash, unsupported);
          const sidecarBytes = encode(value);
          const resourceProof = resource.validateServerResourceEvidence(value, hash(sidecarBytes), workerHash, cell, cohort, index + 1, !unsupported);
          assert.deepEqual(resourceProof, expectedResource(value, hash(sidecarBytes), !unsupported));
          const directory = path.join(input, 'workers', cell.id);
          await mkdir(directory, { recursive: true });
          await writeFile(path.join(directory, 'worker.json'), bytes);
          await writeFile(path.join(directory, 'server-resource-evidence.json'), sidecarBytes);
          const item = { id: cell.id,
            job: { id: index + 1, name: isolatedEvidenceJobName(cell),
              url: 'https://github.com/managedcode/KeyLoad/actions/runs/' + cohort.runId + '/job/' + (index + 1),
              conclusion: 'failure', steps: [{ name: 'Run database workload', conclusion: 'failure' },
                { name: 'Save benchmark results', conclusion: 'success' }] },
            artifact: { id: index + 1001, name: 'comparison-worker-' + cell.id, sizeInBytes: 1,
              digest: 'sha256:' + 'd'.repeat(64), expired: false }, workerSha256: workerHash, serverResource: resourceProof };
          proof.cells.push(item);
          originalCells.set(cell.id, { value, sidecarBytes, item });
        }
        await writeFile(proofFile, encode(proof));
        const tree = async directory =>
        {
          const rows = [];
          async function visit(relative)
          {
            for (const entry of (await readdir(path.join(directory, relative), { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name)))
            {
              const name = path.join(relative, entry.name);
              assert.ok(!entry.isSymbolicLink());
              if (entry.isDirectory()) await visit(name);
              else { assert.ok(entry.isFile()); rows.push([name, hash(await readFile(path.join(directory, name)))]); }
            }
          }
          await visit('');
          return rows;
        };
        const originals = await tree(input);
        const originalPlan = await readFile(planFile);
        const originalProof = await readFile(proofFile);
        const run = output => aggregate.aggregateEvidence({ input, output, plan: planFile, proof: proofFile });
        const originalManifest = await run(originalOutput);
        assert.equal(originalManifest.workers.length, plan.cells.length);
        assert.ok(originalManifest.workers.every(worker => worker.disposition === 'failed'));
        const originalPublished = await tree(originalOutput);
        const selected = plan.cells.find(cell => cell.target === 'KeyLoad' && cell.nodeCount === 3 && cell.scenario === 'PointRead');
        assert.ok(selected);
        const frozen = originalCells.get(selected.id);
        const damaged = structuredClone(frozen.value);
        damaged.containers[duplicateIndex].containerId = damaged.containers[0].containerId;
        assert.equal(new Set(damaged.containers.map(row => row.resource)).size, 3);
        assert.equal(new Set(damaged.containers.map(row => row.containerId)).size, 2);
        const damagedBytes = encode(damaged);
        const sidecarFile = path.join(input, 'workers', selected.id, 'server-resource-evidence.json');
        await writeFile(sidecarFile, damagedBytes);
        const changedProof = structuredClone(proof);
        changedProof.cells.find(item => item.id === selected.id).serverResource.sha256 = hash(damagedBytes);
        await writeFile(proofFile, encode(changedProof));
        const changedInputs = await tree(input);
        const changedProofBytes = await readFile(proofFile);
        assert.throws(() => resource.validateServerResourceEvidence(damaged, hash(damagedBytes), frozen.item.workerSha256,
          selected, cohort, frozen.item.job.id, true), { message: 'Isolated GitHub evidence rejected.' });
        await assert.rejects(run(rejectedOutput), { message: 'Isolated GitHub evidence rejected.' });
        assert.deepEqual(await tree(input), changedInputs);
        assert.deepEqual(await readFile(planFile), originalPlan);
        assert.deepEqual(await readFile(proofFile), changedProofBytes);
        assert.deepEqual(await tree(originalOutput), originalPublished);
        await assert.rejects(access(rejectedOutput), { code: 'ENOENT' });
        assert.ok(!(await readdir(root)).some(name => name.startsWith('.isolated-aggregate-')));
        await writeFile(sidecarFile, frozen.sidecarBytes);
        await writeFile(proofFile, originalProof);
        assert.deepEqual(await tree(input), originals);
        assert.deepEqual(await readFile(proofFile), originalProof);
        const repaired = JSON.parse((await readFile(sidecarFile)).toString('utf8'));
        assert.deepEqual(resource.validateServerResourceEvidence(repaired, hash(frozen.sidecarBytes), frozen.item.workerSha256,
          selected, cohort, frozen.item.job.id, true), frozen.item.serverResource);
        const repairedManifest = await run(repairedOutput);
        assert.deepEqual(repairedManifest, originalManifest);
        assert.deepEqual(await tree(repairedOutput), originalPublished);
        assert.deepEqual(await tree(input), originals);
        assert.deepEqual(await tree(originalOutput), originalPublished);
        process.stdout.write(JSON.stringify({ profile: plan.profile, cells: plan.cells.length,
          nodes: 3, duplicatedOrdinal: duplicateIndex + 1, allReportsNull: true, originalInputsPreserved: true,
          originalAggregatePreserved: true, rejectedOutputAbsent: true, exactRepair: true }));
        """;
}
