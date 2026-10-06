namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedCurrentJobCacheNodeProgram
{
    internal const string Source = """
        import { existsSync, watch } from 'node:fs';
        import { readFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [operation, ...args] = process.argv.slice(1);
        const result = operation === 'policy' ? await runPolicy(args)
          : operation === 'wait-cancel' ? await runWaitCancellation(args)
          : await runOwnedChild(args);
        process.stdout.write(JSON.stringify({ result }));

        async function runPolicy([currentJobModule, transportModule, fixtureDirectory]) {
          const [{ classifyCurrentJob }, { nativeArguments }] = await Promise.all([
            import(pathToFileURL(currentJobModule).href), import(pathToFileURL(transportModule).href)]);
          const cohort = JSON.parse(await readFile(path.join(fixtureDirectory,
            'IsolatedCurrentJobOriginalOpenSearchCohort.json'), 'utf8'));
          const queued = await Promise.all([1, 2, 3].map(async index => JSON.parse(await readFile(
            path.join(fixtureDirectory, `IsolatedCurrentJobOriginalOpenSearchQueued-0${index}.json`), 'utf8'))));
          const headers = await Promise.all([1, 2, 3].map(index => readFile(path.join(fixtureDirectory,
            `IsolatedCurrentJobOriginalOpenSearchQueued-0${index}.headers`), 'utf8')));
          const running = JSON.parse(await readFile(path.join(fixtureDirectory,
            'IsolatedCurrentJobOriginalOpenSearchRunning.json'), 'utf8'));
          const classify = job => classifyCurrentJob(job, cohort, job.name, job.id);
          const constructedRunning = { ...queued[0], status: 'in_progress', conclusion: null };
          const context = { transport: { allowBinary: false } };
          const currentArgs = nativeArguments(`repos/managedcode/KeyLoad/actions/jobs/${queued[0].id}`,
            context, queued[0].id);
          const genericArgs = nativeArguments('repos/managedcode/KeyLoad/actions/runs/37320853130', context);
          let wrongRouteRejected = false;
          try { nativeArguments('repos/managedcode/KeyLoad/actions/runs/37320853130', context, queued[0].id); }
          catch { wrongRouteRejected = true; }
          const mutateAndReject = change => {
            const altered = { ...queued[0], ...change };
            try { classifyCurrentJob(altered, cohort, queued[0].name, queued[0].id); return false; }
            catch { return true; }
          };
          return {
            queuedStates: queued.map(classify), queuedIds: queued.map(job => job.id), runningId: running.id,
            queuedEtags: headers.map(value => value.match(/^ETag:\s*(.+)$/im)?.[1] ?? null),
            cacheControls: headers.map(value => value.match(/^Cache-Control:\s*(.+)$/im)?.[1] ?? null),
            runningState: classify(running), constructedSameIdentity: [classify(queued[0]), classify(constructedRunning)],
            constructedSameId: queued[0].id === constructedRunning.id,
            revalidationHeader: currentArgs.includes('Cache-Control: no-cache, max-age=0'),
            genericHeaderAbsent: !genericArgs.includes('Cache-Control: no-cache, max-age=0'), wrongRouteRejected,
            wrongIdRejected: mutateAndReject({ id: queued[0].id + 1 }),
            wrongRunRejected: mutateAndReject({ run_id: cohort.runId + 1 }),
            wrongAttemptRejected: mutateAndReject({ run_attempt: cohort.attempt + 1 }),
            wrongSourceRejected: mutateAndReject({ head_sha: '0000000000000000000000000000000000000000' }),
            wrongNameRejected: mutateAndReject({ name: 'other job' }),
            wrongWorkflowRejected: mutateAndReject({ workflow_name: 'other workflow' }),
            wrongBranchRejected: mutateAndReject({ head_branch: 'feature' }),
            wrongUrlRejected: mutateAndReject({ html_url: 'https://github.com/managedcode/KeyLoad/runs/1/jobs/1' }),
            terminalRejected: mutateAndReject({ status: 'completed', conclusion: 'failure' }),
          };
        }

        async function runOwnedChild([streamModule, outputPath, markerPath, readyPath, mode, maximumBytes]) {
          const { streamToFile } = await import(pathToFileURL(streamModule).href);
          const controller = new AbortController();
          const childCode = `const fs=require('node:fs');const marker=${JSON.stringify(markerPath)};` +
            `process.once('SIGTERM',()=>{fs.writeFileSync(marker,'terminated');process.exit(0)});` +
            `fs.writeFileSync(${JSON.stringify(readyPath)},'ready');` +
            (mode === 'overflow' ? `process.stdout.write('x'.repeat(4096));` : '') + `setInterval(()=>{},1000);`;
          const task = streamToFile(process.execPath, ['-e', childCode], outputPath,
            Number(maximumBytes), 10000, process.cwd(), controller.signal);
          const settled = task.then(() => ({ success: true }), () => ({ success: false }));
          let ready = false;
          try {
            const readiness = waitForFile(readyPath, controller.signal).then(
              () => ({ ready: true }), failure => ({ ready: false, failure }));
            const readyOutcome = await Promise.race([readiness,
              settled.then(() => ({ ready: false, childSettled: true }))]);
            if (!readyOutcome.ready && !existsSync(readyPath)) {
              if (readyOutcome.failure) throw readyOutcome.failure;
              throw new Error('The native child exited before readiness.');
            }
            ready = true;
            if (mode === 'cancel') controller.abort();
            const captureOutcome = await settled;
            let marker = '';
            try { marker = await readFile(markerPath, 'utf8'); } catch { }
            return { ready, rejected: !captureOutcome.success, marker };
          } finally {
            controller.abort();
            await settled;
          }
        }

        function waitForFile(file, signal) {
          if (existsSync(file)) return Promise.resolve();
          signal.throwIfAborted();
          return new Promise((resolve, reject) => {
            let watcher;
            const complete = failure => {
              clearTimeout(timer);
              watcher?.close();
              signal.removeEventListener('abort', abort);
              if (failure) reject(failure); else resolve();
            };
            const abort = () => complete(signal.reason instanceof Error ? signal.reason
              : new Error('Native child readiness was canceled.'));
            const timer = setTimeout(() => complete(new Error('Native child readiness exceeded its bound.')), 5000);
            signal.addEventListener('abort', abort, { once: true });
            try {
              watcher = watch(path.dirname(file), (_event, name) => {
                if (name === path.basename(file) && existsSync(file)) complete();
              });
              watcher.once('error', complete);
              if (existsSync(file)) complete();
            } catch (error) { complete(error); }
          });
        }

        async function runWaitCancellation([currentJobModule]) {
          const { waitForRefresh } = await import(pathToFileURL(currentJobModule).href);
          const controller = new AbortController();
          const timer = setTimeout(() => controller.abort(), 100);
          let rejected = false;
          try { await waitForRefresh(controller.signal, 1000); } catch { rejected = true; }
          finally { clearTimeout(timer); }
          return { rejected };
        }
        """;
}
