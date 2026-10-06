namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanMatrixNodeProgram
{
    internal const string Source = """
        import { readFileSync } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        const invalidPlanMessage = 'The isolated comparison plan is invalid.';
        const moduleUrl = pathToFileURL(process.env.KEYLOAD_OPEN_LOOP_PLAN_MODULE);
        const input = JSON.parse(readFileSync(0, 'utf8'));
        const baseModule = await import(new URL('./isolated-plan.mjs', moduleUrl).href);
        const scaleModule = await import(new URL('./scaled-isolated-plan.mjs', moduleUrl).href);
        const vectorModule = await import(new URL('./vector-isolated-plan.mjs', moduleUrl).href);
        const preflightModule = await import(new URL('./isolated-preflight.mjs', moduleUrl).href);
        function matrixKind(row) {
          return row.preflight ? 'preflight' : row.openLoopCancellationProof ? 'proof'
            : row.openLoopRate === undefined ? 'worker' : 'open-loop';
        }
        try {
          let matrices;
          const groupKey = process.env.KEYLOAD_MATRIX_GROUP;
          if (groupKey) {
            const plan = JSON.parse(readFileSync(process.env.KEYLOAD_MATRIX_PLAN_PATH, 'utf8'));
            const scales = JSON.parse(readFileSync(process.env.KEYLOAD_MATRIX_SCALE_PATH, 'utf8'));
            const vectors = JSON.parse(readFileSync(process.env.KEYLOAD_MATRIX_VECTOR_PATH, 'utf8'));
            const openLoopPath = process.env.KEYLOAD_MATRIX_OPEN_LOOP_PATH;
            const openLoop = openLoopPath ? JSON.parse(readFileSync(openLoopPath, 'utf8')) : undefined;
            matrices = preflightModule.createDatabaseMatrices(plan, scales, vectors, openLoop);
            const fullRows = matrices[groupKey]?.include;
            if (!Array.isArray(fullRows)) throw new Error(invalidPlanMessage);
            const projection = fullRows.map(row => ({ id: row.id, jobName: row.jobName,
              target: row.target, kind: matrixKind(row) }));
            if (JSON.stringify(projection) !== JSON.stringify(input.include)) throw new Error(invalidPlanMessage);
            process.stdout.write(JSON.stringify({ rejected: false, include: fullRows }) + '\n');
          } else {
            matrices = preflightModule.createDatabaseMatrices(baseModule.createIsolatedPlan(),
              scaleModule.createScaledPlans(), vectorModule.createVectorPlans(), input.openLoopPlan);
            const counts = Object.fromEntries(Object.entries(matrices).map(([key, value]) => [key, value.include.length]));
            process.stdout.write(JSON.stringify({ rejected: false, counts }) + '\n');
          }
        } catch (error) {
          if (!(error instanceof Error) || error.message !== invalidPlanMessage) throw error;
          process.stdout.write(JSON.stringify({ rejected: true }) + '\n');
          process.exitCode = 0;
        }
        """;
}
