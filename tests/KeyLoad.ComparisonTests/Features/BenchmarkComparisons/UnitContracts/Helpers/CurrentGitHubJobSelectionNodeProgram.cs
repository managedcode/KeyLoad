namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class CurrentGitHubJobSelectionNodeProgram
{
    internal const string Source = """
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';

        const root = process.env.KEYLOAD_CURRENT_JOB_SELECTION_ROOT;
        const modulePath = relative => pathToFileURL(path.join(root, relative)).href;
        const [{ selectCurrentJob }, { createGitHubContext }, { createDatabaseMatrices }] = await Promise.all([
          import(modulePath('scripts/Features/BenchmarkComparisons/isolated-github-job-selection.mjs')),
          import(modulePath('scripts/Features/BenchmarkComparisons/isolated-github-context.mjs')),
          import(modulePath('scripts/Features/BenchmarkComparisons/isolated-preflight.mjs')),
        ]);

        const context = createGitHubContext({
          GH_TOKEN: 'selection-regression-token',
          GITHUB_WORKFLOW: 'Benchmarks',
          GITHUB_SHA: 'a'.repeat(40),
          GITHUB_RUN_ID: '37420525127',
          GITHUB_RUN_ATTEMPT: '1',
          GITHUB_REPOSITORY: 'managedcode/KeyLoad',
          GITHUB_REF: 'refs/heads/main',
          RUNNER_TEMP: path.join(root, 'selection-runner-temp'),
          GITHUB_WORKSPACE: root,
          GITHUB_OUTPUT: path.join(root, 'selection-output'),
        }, 'linux');
        const matrices = createDatabaseMatrices(context.plan, context.scaledPlans,
          context.vectorPlans, context.openLoopPlan);
        const rows = Object.values(matrices).flatMap(matrix => matrix.include);
        const row = family => {
          const matches = rows.filter(item => {
            if (family === 'preflight') return item.preflight === true;
            if (family === 'control') return item.preflight !== true && item.profile === context.plan.profile
              && item.openLoopRate === undefined;
            if (family === 'scaled') return item.scaleProfile !== null;
            if (family === 'vector') return item.vectorProfile !== null;
            if (family === 'openloop') return item.openLoopRate !== undefined
              && item.openLoopCancellationProof !== true;
            if (family === 'proof') return item.openLoopCancellationProof === true;
            return false;
          });
          if (matches.length === 0) throw new Error('A canonical job row family was absent.');
          return matches[0];
        };
        const workerEnvironment = value => ({
          KEYLOAD_COMPARISON_JOB_NAME: value.jobName,
          KEYLOAD_COMPARISON_CELL_ID: value.id,
          Benchmarks__EvidenceProfile: value.profile,
          KEYLOAD_SCALE_PROFILE: value.scaleProfile ?? '',
          KEYLOAD_VECTOR_PROFILE: value.vectorProfile ?? '',
          KEYLOAD_OPEN_LOOP_RATE: value.openLoopRate === undefined ? '' : String(value.openLoopRate),
          KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF: value.openLoopCancellationProof === undefined
            ? '' : String(value.openLoopCancellationProof),
          Benchmarks__NodeCount: String(value.nodeCount),
          Benchmarks__Scenario: value.scenario,
          Benchmarks__VectorProfile: value.vectorProfile ?? '',
          Benchmarks__Target: value.target,
          KEYLOAD_MATRIX_KIND: value.preflight ? 'preflight' : value.openLoopCancellationProof ? 'proof'
            : value.openLoopRate === undefined ? 'worker' : 'open-loop',
        });
        const execute = (name, environment, selectedContext = context, expectedProfile = null) => {
          const before = JSON.stringify(selectedContext);
          try {
            const selection = selectCurrentJob(environment, selectedContext);
            return { name, selectedJobName: selection.name, accepted: true,
              selectedProfile: selection.context.cohort.profile,
              expectedProfile,
              contextUnchanged: before === JSON.stringify(selectedContext),
              reusesInputContext: selection.context === selectedContext };
          } catch {
            return { name, accepted: false, contextUnchanged: before === JSON.stringify(selectedContext),
              reusesInputContext: false };
          }
        };

        const preflight = row('preflight');
        const control = row('control');
        const scaled = row('scaled');
        const vector = row('vector');
        const openLoop = row('openloop');
        const proof = row('proof');
        const imageName = 'Build Docker images';
        const imageEnvironment = { KEYLOAD_COMPARISON_JOB_NAME: imageName };
        const profileMismatch = workerEnvironment(control);
        profileMismatch.Benchmarks__EvidenceProfile = scaled.profile;
        const cellMismatch = workerEnvironment(control);
        cellMismatch.KEYLOAD_COMPARISON_CELL_ID = scaled.id;
        const nameMismatch = workerEnvironment(control);
        nameMismatch.KEYLOAD_COMPARISON_JOB_NAME = scaled.jobName;
        const selectorMismatch = workerEnvironment(control);
        selectorMismatch.KEYLOAD_SCALE_PROFILE = scaled.profile;
        const targetMismatch = workerEnvironment(control);
        const differentTarget = rows.find(item => item.target !== control.target);
        if (!differentTarget) throw new Error('A different canonical database target was absent.');
        targetMismatch.Benchmarks__Target = differentTarget.target;
        const kindMismatch = workerEnvironment(control);
        kindMismatch.KEYLOAD_MATRIX_KIND = 'open-loop';
        const missingTarget = workerEnvironment(control);
        delete missingTarget.Benchmarks__Target;
        const missingKind = workerEnvironment(control);
        delete missingKind.KEYLOAD_MATRIX_KIND;

        const results = [
          execute('image', imageEnvironment, context, context.plan.profile),
          ...['preflight', 'control', 'scaled', 'vector', 'openloop', 'proof'].map(family =>
            execute(family, workerEnvironment(row(family)), context, row(family).profile)),
          execute('unknown-job', { ...workerEnvironment(control), KEYLOAD_COMPARISON_JOB_NAME: 'unknown-job' }),
          execute('missing-cell-id', { KEYLOAD_COMPARISON_JOB_NAME: control.jobName,
            Benchmarks__EvidenceProfile: control.profile }),
          execute('missing-evidence-profile', { KEYLOAD_COMPARISON_JOB_NAME: control.jobName,
            KEYLOAD_COMPARISON_CELL_ID: control.id }),
          execute('missing-target', missingTarget),
          execute('missing-matrix-kind', missingKind),
          execute('mismatched-job-name', nameMismatch),
          execute('mismatched-cell-id', cellMismatch),
          execute('mismatched-evidence-profile', profileMismatch),
          execute('mismatched-row-selector', selectorMismatch),
          execute('mismatched-target', targetMismatch),
          execute('mismatched-matrix-kind', kindMismatch),
          execute('unknown-cell-id', { ...workerEnvironment(control), KEYLOAD_COMPARISON_CELL_ID: 'unknown-cell' }),
          execute('image-with-cell-id', { ...imageEnvironment, KEYLOAD_COMPARISON_CELL_ID: control.id }),
          execute('image-with-evidence-profile', { ...imageEnvironment,
            Benchmarks__EvidenceProfile: control.profile }),
          execute('image-with-scale-profile', { ...imageEnvironment, KEYLOAD_SCALE_PROFILE: scaled.profile }),
        ];
        process.stdout.write(JSON.stringify(results) + '\n');
        """;
}
