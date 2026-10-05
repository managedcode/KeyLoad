using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-VQ-007: historical contracts require their original source/blob bytes and cannot satisfy current scale claims.</summary>
internal sealed class HistoricalBenchmarkContractTests
{
    private const string Probe = """
        import {readFileSync} from 'node:fs';
        import {pathToFileURL} from 'node:url';
        const module=await import(pathToFileURL(process.argv[1]).href);
        const source=module.HISTORICAL.sourceRevisions[3];
        const bytes=readFileSync(new URL('./historical-isolated-contract.json',pathToFileURL(process.argv[1])));
        const capture={type:'file',path:module.HISTORICAL.path,name:'isolated-contract.json',sha:module.HISTORICAL.gitBlob,
          encoding:'base64',size:bytes.length,html_url:'https://github.com/managedcode/KeyLoad/blob/'+source+'/'+module.HISTORICAL.path,
          git_url:'https://api.github.com/repos/managedcode/KeyLoad/git/blobs/'+module.HISTORICAL.gitBlob,content:bytes.toString('base64')};
        const contract=module.verifyHistoricalContractCapture(capture,source);
        const plan=module.createHistoricalPlan(source);
        const rejects=(operation)=>{try{operation();return false;}catch{return true;}};
        const altered=Buffer.from(bytes);altered[0]=32;
        const alteredPlan=structuredClone(plan);alteredPlan.cells[0].target='SurrealDB';
        process.stdout.write(JSON.stringify({count:plan.cells.length,targets:contract.targets.length,workerSchema:contract.workerSchemaVersion,
          rejectsBytes:rejects(()=>module.verifyHistoricalContractCapture({...capture,content:altered.toString('base64')},source)),
          rejectsBlob:rejects(()=>module.verifyHistoricalContractCapture({...capture,sha:'b'.repeat(40)},source)),
          rejectsSource:rejects(()=>module.createHistoricalPlan('a'.repeat(40))),
          rejectsPlan:rejects(()=>module.validateHistoricalPlan(alteredPlan,source)),
          vectors:plan.cells.filter(cell=>cell.profile.startsWith('vector-')).length,
          scales:plan.cells.filter(cell=>cell.profile.startsWith('scaled-')).length}));
        """;

    [Test]
    public async Task OriginalHistoricalSourceCannotBeRelabeledAsCurrentVectorScaleQualification()
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(["--input-type=module", "-e", Probe,
            IsolatedAggregateNodeProcess.Module("historical-isolated-plan.mjs")], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0).Because(response.Error);
        using var document = JsonDocument.Parse(response.Output);
        var value = document.RootElement;
        await Assert.That(value.GetProperty("count").GetInt32()).IsEqualTo(270);
        await Assert.That(value.GetProperty("targets").GetInt32()).IsEqualTo(9);
        await Assert.That(value.GetProperty("workerSchema").GetInt32()).IsEqualTo(4);
        await Assert.That(value.GetProperty("vectors").GetInt32()).IsEqualTo(0);
        await Assert.That(value.GetProperty("scales").GetInt32()).IsEqualTo(0);
        await Assert.That(value.GetProperty("rejectsBytes").GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty("rejectsBlob").GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty("rejectsSource").GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty("rejectsPlan").GetBoolean()).IsTrue();
    }
}
