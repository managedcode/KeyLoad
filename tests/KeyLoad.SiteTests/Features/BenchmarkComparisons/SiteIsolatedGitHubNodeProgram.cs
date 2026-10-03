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
          else if (request.operation === 'clone') result = await clone(request.arguments.input,request.arguments.output);
          else if (request.operation === 'detach') result = await detach(request.arguments.path);
          else throw new Error('Unknown controlled probe operation.');
          process.stdout.write(JSON.stringify({ok:true,result}));
        } catch { process.stdout.write(JSON.stringify({ok:false})); }
        """;
}
