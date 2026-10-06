namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanNodeProgram
{
    internal const string CreateOperation = "create";
    internal const string ScaledOperation = "scaled";
    internal const string ValidateOperation = "validate";
    internal const string CancelBeforePublishOperation = "cancel-before-publish";
    internal const string ReadyMarker = "open-loop-plan-ready";
    internal const string ModuleEnvironment = "KEYLOAD_OPEN_LOOP_PLAN_MODULE";
    internal const string OperationEnvironment = "KEYLOAD_OPEN_LOOP_PLAN_OPERATION";
    internal const string OutputEnvironment = "KEYLOAD_OPEN_LOOP_PLAN_OUTPUT";
    internal const string Source = """
        import { readFileSync } from 'node:fs';
        import { writeFile } from 'node:fs/promises';
        import { pathToFileURL } from 'node:url';
        const moduleUrl = pathToFileURL(process.env.KEYLOAD_OPEN_LOOP_PLAN_MODULE);
        const operation = process.env.KEYLOAD_OPEN_LOOP_PLAN_OPERATION;
        const cancelBeforePublishOperation = 'cancel-before-publish';
        const readyMarker = 'open-loop-plan-ready';
        const validationFailure = 'The isolated comparison plan is invalid.';
        if (operation === 'scaled') {
          const scaled = await import(new URL('./scaled-isolated-plan.mjs', moduleUrl));
          process.stdout.write(JSON.stringify(scaled.createScaledPlans()) + '\n');
        } else {
          const planner = await import(moduleUrl.href);
          if (operation === 'create') {
            const source = await import(new URL('./isolated-plan.mjs', moduleUrl));
            const callerContract = source.readIsolatedContract();
            const plan = planner.createOpenLoopPlan(callerContract);
            const firstTarget = plan.measurementCells[0].target;
            callerContract.targets[0] = 'mutated-after-planning';
            if (plan.measurementCells[0].target !== firstTarget) throw new Error('The generated plan retained its input.');
            const validated = planner.validateOpenLoopPlan(plan);
            validated.measurementCells[0].target = 'mutated-validation-copy';
            const canonical = planner.validateOpenLoopPlan(plan);
            await writeFile(process.env.KEYLOAD_OPEN_LOOP_PLAN_OUTPUT, JSON.stringify(canonical) + '\n', { flag: 'wx' });
            process.stdout.write('{"created":true}\n');
          } else if (operation === cancelBeforePublishOperation) {
            const source = await import(new URL('./isolated-plan.mjs', moduleUrl));
            const callerContract = source.readIsolatedContract();
            const plan = planner.createOpenLoopPlan(callerContract);
            if (plan.measurementCells.length === 0) throw new Error('The canonical plan is empty.');
            const inputEnded = new Promise(resolve => process.stdin.once('end', resolve));
            process.stdin.resume();
            process.stdout.write(`${readyMarker}\n`);
            await inputEnded;
            const canonical = planner.validateOpenLoopPlan(plan);
            await writeFile(process.env.KEYLOAD_OPEN_LOOP_PLAN_OUTPUT, JSON.stringify(canonical) + '\n', { flag: 'wx' });
          } else if (operation === 'validate') {
            const candidate = JSON.parse(readFileSync(0, 'utf8'));
            let rejected = false;
            try { planner.validateOpenLoopPlan(candidate); }
            catch (error) {
              if (!(error instanceof Error) || error.message !== validationFailure) throw error;
              rejected = true;
            }
            if (!rejected) {
              const canonical = planner.validateOpenLoopPlan(candidate);
              await writeFile(process.env.KEYLOAD_OPEN_LOOP_PLAN_OUTPUT, JSON.stringify(canonical) + '\n', { flag: 'wx' });
            }
            process.stdout.write(JSON.stringify({ rejected }) + '\n');
          } else {
            throw new Error('Unknown open-loop plan process operation.');
          }
        }
        """;
}
