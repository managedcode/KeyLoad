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
        let matrices;
        try {
          matrices = preflightModule.createDatabaseMatrices(baseModule.createIsolatedPlan(),
            scaleModule.createScaledPlans(), vectorModule.createVectorPlans(), input.openLoopPlan);
        } catch (error) {
          if (!(error instanceof Error) || error.message !== invalidPlanMessage) throw error;
          process.stdout.write(JSON.stringify({ rejected: true }) + '\n');
          process.exitCode = 0;
        }
        if (matrices !== undefined) {
          const counts = Object.fromEntries(Object.entries(matrices).map(([key, value]) => [key, value.include.length]));
          process.stdout.write(JSON.stringify({ rejected: false, counts }) + '\n');
        }
        """;
}
