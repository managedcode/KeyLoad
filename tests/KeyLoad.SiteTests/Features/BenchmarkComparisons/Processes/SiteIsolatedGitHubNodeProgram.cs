namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

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
          const files = [...receipt.metadataFiles,...Object.values(receipt.archives),...receipt.inputFiles];
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
          else if (request.operation === 'selection') {
            const selection = await (await load('runs')).selectSiteIsolatedEvidence(request.arguments);
            result = selection.state === 'selected' ? {state:selection.state,runId:selection.run.id,
              attempt:selection.run.run_attempt,aggregateJobId:selection.job.id} : selection;
          }
          else if (request.operation === 'proof') {
            const selection = await (await load('runs')).selectSiteIsolatedEvidence(request.arguments);
            result = await (await load('proof')).proveSiteIsolatedEvidence({...request.arguments,selection});
          }
          else if (request.operation === 'inputs') result = await (await load('cli')).runSiteIsolatedGitHub([
            'verify-inputs','--input='+request.arguments.input,'--receipt='+request.arguments.receipt]);
          else if (request.operation === 'fresh') result = await (await load('cli')).runSiteIsolatedGitHub([
            'fresh','--before='+request.arguments.before,'--after='+request.arguments.after]);
          else if (request.operation === 'cli') result = await (await load('cli')).runSiteIsolatedGitHub(request.arguments);
          else if (request.operation === 'executor-admission') {
            const { SITE_GH } = await load('contract');
            const { createSiteIsolatedContext } = await load('context');
            const revision = 'a'.repeat(40);
            const foreignWorkflowPath = '.github/workflows/foreign.yml';
            const invalidWorkflowPath = '.github/workflows/not-website.yml';
            const makeFixture = () => ({
              environment: {
                GITHUB_REPOSITORY: SITE_GH.repository, GH_REPO: SITE_GH.repository,
                GITHUB_REPOSITORY_ID: String(SITE_GH.repositoryId), GITHUB_ACTIONS: 'true', RUNNER_OS: 'Linux',
                GITHUB_WORKFLOW: SITE_GH.executor, GITHUB_WORKFLOW_SHA: revision,
                GITHUB_WORKFLOW_REF: `${SITE_GH.repository}/${SITE_GH.executorPath}@refs/heads/main`,
                GITHUB_JOB: SITE_GH.executorJobs[0], GITHUB_EVENT_NAME: 'push', GITHUB_SHA: revision,
                GH_TOKEN: 'controlled-test-token', GITHUB_RUN_ID: '1', GITHUB_RUN_ATTEMPT: '1',
                GITHUB_WORKSPACE: '/tmp/keyload-site-tests', GITHUB_REF: 'refs/heads/main',
              },
              args: { mode: SITE_GH.publish, 'workflow-revision': revision, 'site-revision': revision },
            });
            const candidate = makeFixture();
            if (request.arguments.identity === 'foreign-executor') {
              candidate.environment.GITHUB_WORKFLOW = 'Foreign executor';
              candidate.environment.GITHUB_WORKFLOW_REF = `${SITE_GH.repository}/${foreignWorkflowPath}@refs/heads/main`;
            } else if (request.arguments.identity === 'wrong-path') {
              candidate.environment.GITHUB_WORKFLOW_REF = `${SITE_GH.repository}/${invalidWorkflowPath}@refs/heads/main`;
            }
            const before = JSON.stringify(candidate);
            let candidateAccepted = true;
            try { await createSiteIsolatedContext(candidate.environment, candidate.args, 'linux'); }
            catch { candidateAccepted = false; }
            const inputsPreserved = JSON.stringify(candidate) === before;
            const followup = makeFixture();
            const healthy = await createSiteIsolatedContext(followup.environment, followup.args, 'linux');
            result = { candidateAccepted, inputsPreserved, followupAccepted: healthy.executor.workflow === SITE_GH.executor
              && healthy.executor.sourceRevision === revision && healthy.executor.event === 'push' };
          }
          else if (request.operation === 'latest-selection') {
            const runs = await load('runs');
            const metadata = path.join(request.arguments.input,'metadata');
            const workflow = runs.validateSiteWorkflow(JSON.parse(await readFile(path.join(metadata,'workflow.json'),'utf8')));
            const pages = JSON.parse(await readFile(path.join(metadata,'workflow_runs-pages.json'),'utf8'));
            const producer = runs.selectLatestSiteProducer(runs.flattenSiteRuns(pages),workflow);
            const selection = await runs.selectSiteIsolatedEvidence({input:request.arguments.input,mode:'publish',producer});
            result = {producer,state:selection.state,runId:selection.run?.id,attempt:selection.run?.run_attempt};
          }
          else if (request.operation === 'clone') result = await clone(request.arguments.input,request.arguments.output);
          else if (request.operation === 'detach') result = await detach(request.arguments.path);
          else throw new Error('Unknown controlled probe operation.');
          process.stdout.write(JSON.stringify({ok:true,result}));
        } catch { process.stdout.write(JSON.stringify({ok:false})); }
        """;
}
