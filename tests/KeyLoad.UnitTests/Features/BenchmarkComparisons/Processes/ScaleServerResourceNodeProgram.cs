namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceNodeProgram
{
    internal const string Source = """
        import { pathToFileURL } from 'node:url';
        const { validateServerResourceEvidence, requireComparableScaleProfiles } = await import(pathToFileURL(process.argv[1]));
        const scenario = process.argv[2];
        const cell = { target: 'KeyLoad', nodeCount: 1, scenario: 'DocumentWrite', profile: 'scaled-100k-c16' };
        const otherTargetCell = { ...cell, target: 'Redis' };
        const cohort = { sourceRevision: 'c'.repeat(40), runId: 123, attempt: 2 };
        const workerSha = 'a'.repeat(64);
        const sidecarSha = 'd'.repeat(64);
        const hardware = { kernel: '6.8.0', architecture: 'X64', cpuVendor: 'GenuineIntel', cpuFamily: 6,
          cpuModel: 143, cpuStepping: 0, logicalCpuCount: 2, physicalCoreCount: 1,
          logicalCpuMembership: '0,1', physicalCoreMembership: ['0:0'], memoryBytes: 8192 };
        const envelope = { cpuQuota: '2', cpuSet: '0-1', memoryLimitBytes: '8192' };
        const mount = { containerPath: '/data', mountType: 'bind', fileSystem: 'ext4', capacityBytes: 8192, availableBytes: 4096 };
        const container = { resource: 'node-1', containerId: 'b'.repeat(64), imageId: `sha256:${'e'.repeat(64)}`,
          startedAt: '2026-10-05T00:00:00Z', state: 'running', cpuLimit: '2 100000', memoryLimitBytes: '8192',
          effectiveCpuQuota: '2', effectiveCpuSet: '0-1', effectiveMemoryLimitBytes: '8192',
          observedCpuUsageUsec: 30, maxObservedCgroupMemoryBytes: 512, maxObservedRssBytes: 256,
          sampleCount: 2, writableMounts: [mount] };
        const evidence = () => ({ schema: 'server-resource-evidence.v1', sourceRevision: cohort.sourceRevision,
          workflowRunId: String(cohort.runId), runAttempt: String(cohort.attempt), jobId: '456', target: cell.target,
          nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile, workerSha256: workerSha,
          hardware: structuredClone(hardware), appHostEnvelope: structuredClone(envelope),
          containers: [structuredClone(container)], missingEvidence: [], qualified: true });
        const configuredEvidence = () => ({ ...evidence(), schema: 'server-resource-evidence.v2', observationPolicy: {
          cadenceMilliseconds: 100, maximumObservationMilliseconds: 1000, cleanupThresholdMilliseconds: 1000,
          processSettlementMilliseconds: 100, maxProcesses: 16, maxMounts: 2, maxFileBytes: 512,
          minimumCommandBytes: 8, maxHardwareBytes: 1024, maxSampleMetadataBytes: 2048, maxSidecarBytes: 8192,
          maxWorkerBytes: 65536, maxSamples: 10, maxCgroupAncestors: 8, nativeReadBufferBytes: 128,
          maxNativeOutputBytes: 256, standardErrorOutputDivisor: 4 } });
        const expectRejected = value => {
          try { validateServerResourceEvidence(value, sidecarSha, workerSha, cell, cohort, 456, true); process.exitCode = 1; }
          catch (error) { if (error.message !== 'Isolated GitHub evidence rejected.') process.exitCode = 1; }
        };
        if (scenario === 'configured-policy') validateServerResourceEvidence(configuredEvidence(), sidecarSha, workerSha, cell, cohort, 456, true);
        else if (scenario === 'invalid-configured-policy') {
          const value = configuredEvidence(); value.observationPolicy.cadenceMilliseconds = 0; expectRejected(value);
        } else if (scenario === 'configured-sample-overflow') {
          const value = configuredEvidence(); value.containers[0].sampleCount = 11; expectRejected(value);
        } else if (scenario === 'mismatched-configured-policy') {
          const left = validateServerResourceEvidence(configuredEvidence(), sidecarSha, workerSha, cell, cohort, 456, true);
          const value = configuredEvidence(); value.jobId = '457'; value.target = otherTargetCell.target;
          value.observationPolicy.cadenceMilliseconds = 200;
          const right = validateServerResourceEvidence(value, '1'.repeat(64), workerSha, otherTargetCell, cohort, 457, true);
          try { requireComparableScaleProfiles([{ profile: cell.profile, workers: [
            { ...cell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: left.comparison },
            { ...otherTargetCell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: right.comparison }] }]); process.exitCode = 1; }
          catch (error) { if (error.message !== 'Isolated GitHub evidence rejected.') process.exitCode = 1; }
        } else if (scenario === 'valid') validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true);
        else if (scenario === 'missing') {
          const value = evidence(); value.hardware = null; value.missingEvidence = ['hardwareClass']; value.qualified = false;
          validateServerResourceEvidence(value, sidecarSha, workerSha, cell, cohort, 456, true);
        } else if (scenario === 'wrong-worker') expectRejected({ ...evidence(), workerSha256: 'f'.repeat(64) });
        else if (scenario === 'wrong-job') { const value = evidence(); value.jobId = '999'; expectRejected(value); }
        else if (scenario === 'one-sample-qualified') {
          const value = evidence(); value.containers[0].sampleCount = 1; expectRejected(value);
        }
        else if (scenario === 'too-many-mounts') {
          const value = evidence(); value.containers[0].writableMounts = Array.from({ length: 9 }, (_, index) =>
            ({ ...mount, containerPath: `/data-${index}` })); expectRejected(value);
        }
        else if (scenario === 'unsupported') {
          const value = evidence(); value.containers = []; value.missingEvidence = ['serverCpuRss']; value.qualified = false;
          validateServerResourceEvidence(value, sidecarSha, workerSha, cell, cohort, 456, false);
        } else if (scenario === 'equivalent-targets') {
          const left = validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true);
          const rightValue = evidence(); rightValue.jobId = '457'; rightValue.target = otherTargetCell.target;
          rightValue.containers[0].resource = 'redis-node-1';
          rightValue.containers[0].containerId = 'f'.repeat(64);
          rightValue.containers[0].cpuLimit = '1 50000';
          rightValue.containers[0].writableMounts[0].containerPath = '/var/lib/redis';
          rightValue.containers[0].writableMounts[0].availableBytes = 2048;
          rightValue.containers[0].observedCpuUsageUsec = 1000; rightValue.containers[0].maxObservedRssBytes = 1000;
          const right = validateServerResourceEvidence(rightValue, '1'.repeat(64), workerSha, otherTargetCell, cohort, 457, true);
          requireComparableScaleProfiles([{ profile: cell.profile,
            workers: [{ ...cell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: left.comparison },
              { ...otherTargetCell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: right.comparison }] }]);
        } else if (scenario === 'separate-profiles') {
          const left = validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true);
          const otherProfile = { ...cell, profile: 'scaled-1m-c16' };
          const value = evidence(); value.profile = otherProfile.profile; value.jobId = '457';
          value.containers[0].effectiveMemoryLimitBytes = '4096';
          const right = validateServerResourceEvidence(value, '1'.repeat(64), workerSha, otherProfile, cohort, 457, true);
          requireComparableScaleProfiles([{ profile: cell.profile, workers: [{ ...cell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: left.comparison }] },
            { profile: otherProfile.profile, workers: [{ ...otherProfile, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: right.comparison }] }]);
        } else if (scenario === 'mismatched') {
          const left = validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true);
          const changed = evidence(); changed.jobId = '457'; changed.target = otherTargetCell.target;
          changed.containers[0].effectiveMemoryLimitBytes = '4096';
          const right = validateServerResourceEvidence(changed, '1'.repeat(64), workerSha, otherTargetCell, cohort, 457, true);
          try { requireComparableScaleProfiles([{ profile: cell.profile,
            workers: [{ ...cell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: left.comparison },
              { ...otherTargetCell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: right.comparison }] }]); process.exitCode = 1; }
          catch (error) { if (error.message !== 'Isolated GitHub evidence rejected.') process.exitCode = 1; }
        } else if (scenario === 'complete-matched-cohort') {
          const targets = ['KeyLoad', 'PostgreSQL + pgvector', 'Qdrant', 'RabbitMQ', 'Redis', 'Neo4j', 'MongoDB', 'OpenSearch', 'KurrentDB'];
          const comparison = validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true).comparison;
          const workers = targets.map(target => ({ target, nodeCount: 1, scenario: cell.scenario,
            disposition: 'measured', serverResourceQualified: true, resourceEquivalence: comparison }));
          requireComparableScaleProfiles([{ profile: cell.profile, workers }]);
        } else if (scenario === 'failed-workload-mismatch') {
          const left = validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true);
          const changed = evidence(); changed.target = otherTargetCell.target; changed.jobId = '457';
          changed.containers[0].effectiveMemoryLimitBytes = '4096';
          const right = validateServerResourceEvidence(changed, '1'.repeat(64), workerSha, otherTargetCell, cohort, 457, true);
          try { requireComparableScaleProfiles([{ profile: cell.profile, workers: [
            { ...cell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: left.comparison },
            { ...otherTargetCell, disposition: 'failed', serverResourceQualified: true, resourceEquivalence: right.comparison }] }]); process.exitCode = 1; }
          catch (error) { if (error.message !== 'Isolated GitHub evidence rejected.') process.exitCode = 1; }
        } else if (scenario === 'retained-incomplete') {
          const left = validateServerResourceEvidence(evidence(), sidecarSha, workerSha, cell, cohort, 456, true);
          requireComparableScaleProfiles([{ profile: cell.profile, workers: [
            { ...cell, disposition: 'measured', serverResourceQualified: true, resourceEquivalence: left.comparison },
            { ...otherTargetCell, disposition: 'unsupportedTopology', serverResourceQualified: false, resourceEquivalence: null },
            { ...otherTargetCell, target: 'MongoDB', disposition: 'failed', serverResourceQualified: false, resourceEquivalence: null }] }]);
        } else if (scenario === 'invalid-cpu-set') {
          const value = evidence(); value.appHostEnvelope.cpuSet = '0-3,2-4'; expectRejected(value);
        } else if (scenario === 'invalid-cpu-membership') {
          const value = evidence(); value.hardware.logicalCpuMembership = '0,0'; expectRejected(value);
        } else if (scenario === 'invalid-cpu-quota') {
          const value = evidence(); value.containers[0].cpuLimit = '0 100000'; expectRejected(value);
        } else if (scenario === 'invalid-cpu-set-membership') {
          const value = evidence(); value.containers[0].effectiveCpuSet = '0-2'; expectRejected(value);
        } else process.exitCode = 1;
        """;
}
