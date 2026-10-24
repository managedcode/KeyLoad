namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubNodeProgram
{
    public const string Source = """
        import { chmod, copyFile, link, lstat, mkdir, readFile, rename } from 'node:fs/promises';
        import { constants } from 'node:fs';
        import { pathToFileURL } from 'node:url';
        import path from 'node:path';
        const request = JSON.parse(await readFile(process.argv[2], 'utf8'));
        const root = path.join(request.repository, 'scripts/Features/BenchmarkComparisons');
        const load = name => import(pathToFileURL(path.join(root, 'site-isolated-github-' + name + '.mjs')));
        async function clone(actual, output) {
          const receipt = JSON.parse(await readFile(path.join(actual, 'archive-receipt.json'), 'utf8'));
          (await load('receipt')).validateSiteIsolatedReceipt(receipt);
          await mkdir(output, {recursive:false});
          const files = [...receipt.metadataFiles,...Object.values(receipt.archives),...receipt.inputFiles,
            ...receipt.workerArchives,receipt.runtimeIdentity.archive];
          for (const file of files) {
            const source = path.join(actual,file.path), target = path.join(output,file.path);
            const info = await lstat(source);
            if (!info.isFile() || info.isSymbolicLink()) throw new Error('Invalid authentic source file.');
            await mkdir(path.dirname(target),{recursive:true});
            await link(source,target);
          }
          for (const file of ['metadata-proof.json','archive-receipt.json']) {
            await copyFile(path.join(actual,file),path.join(output,file),constants.COPYFILE_EXCL);
            await chmod(path.join(output,file),0o600);
          }
          return {files:receipt.inputFiles.length};
        }
        async function detach(file) {
          const privateCopy = file+'.detached';
          await copyFile(file,privateCopy,constants.COPYFILE_EXCL);
          await chmod(privateCopy,0o600);
          await rename(privateCopy,file);
          return {detached:true};
        }
        try {
          let result;
          if (request.operation === 'receipt') result = (await load('receipt')).validateSiteIsolatedReceipt(request.receipt);
          else if (['selection', 'proof'].includes(request.operation)) {
            const module = await import(pathToFileURL(path.join(root, 'site-session-selection.mjs')));
            const { flattenPages } = await import(pathToFileURL(path.join(root, 'isolated-github-validation.mjs')));
            const directory = path.join(request.arguments.input, 'metadata', String(request.arguments.requestedRun));
            const json = async file => JSON.parse(await readFile(file, 'utf8'));
            const run = await json(path.join(directory, 'run-attempt.json'));
            const workflow = await json(path.join(request.arguments.input, 'metadata/workflow.json'));
            const jobs = flattenPages(await json(path.join(directory, 'jobs-pages.json')), 'jobs');
            const artifacts = flattenPages(await json(path.join(directory, 'artifacts-pages.json')), 'artifacts');
            const selected = module.selectSessionSiteProducer({run,jobs,artifacts},workflow);
            if (selected === null) throw new Error('No current session producer.');
            result = {state:'selected',runId:run.id,attempt:run.run_attempt,aggregateJobId:selected.job.id};
          }
          else if (request.operation === 'inputs') result = await (await load('cli')).runSiteIsolatedGitHub([
            'verify-inputs','--input='+request.arguments.input,'--receipt='+request.arguments.receipt]);
          else if (request.operation === 'fresh') result = await (await load('cli')).runSiteIsolatedGitHub([
            'fresh','--before='+request.arguments.before,'--after='+request.arguments.after]);
          else if (request.operation === 'cli') result = await (await load('cli')).runSiteIsolatedGitHub(request.arguments);
          else if (request.operation === 'executor-admission') {
            const { sessionSiteContext } = await import(pathToFileURL(path.join(root, 'site-session-context.mjs')));
            const revision = 'a'.repeat(40), repository = 'managedcode/KeyLoad';
            const makeFixture = () => ({ environment: {
              GITHUB_REPOSITORY:repository,GH_REPO:repository,GITHUB_REPOSITORY_ID:'477801965',
              GITHUB_ACTIONS:'true',RUNNER_OS:'Linux',GITHUB_WORKFLOW:'Website',GITHUB_WORKFLOW_SHA:revision,
              GITHUB_WORKFLOW_REF:repository+'/.github/workflows/website.yml@refs/heads/main',GITHUB_JOB:'qualify',
              GITHUB_EVENT_NAME:'push',GITHUB_SHA:revision,GH_TOKEN:'controlled-test-token',GITHUB_RUN_ID:'1',
              GITHUB_RUN_ATTEMPT:'1',GITHUB_WORKSPACE:request.repository,GITHUB_REF:'refs/heads/main'},
              args:{mode:'publish','workflow-revision':revision,'site-revision':revision,input:request.repository}});
            const candidate = makeFixture();
            if (request.arguments.identity === 'foreign-executor') candidate.environment.GITHUB_WORKFLOW = 'Foreign';
            if (request.arguments.identity === 'wrong-path') candidate.environment.GITHUB_WORKFLOW_REF = repository+'/.github/workflows/foreign.yml@refs/heads/main';
            const before = JSON.stringify(candidate);
            let candidateAccepted = true;
            try { sessionSiteContext(candidate.environment,candidate.args,'linux'); } catch { candidateAccepted = false; }
            const followup = makeFixture();
            const healthy = sessionSiteContext(followup.environment,followup.args,'linux');
            result = {candidateAccepted,inputsPreserved:JSON.stringify(candidate)===before,followupAccepted:healthy.mode==='publish'};
          }
          else if (request.operation === 'clone') result = await clone(request.arguments.input,request.arguments.output);
          else if (request.operation === 'detach') result = await detach(request.arguments.path);
          else throw new Error('Unknown controlled probe operation.');
          process.stdout.write(JSON.stringify({ok:true,result}));
        } catch { process.stdout.write(JSON.stringify({ok:false})); }
        """;
}
