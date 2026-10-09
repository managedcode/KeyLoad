using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-SCALE-015: complete internal receipts preserve failures and disclose missing real evidence.</summary>
internal sealed class ScaledCohortReceiptTests
{
    private const string ModuleMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string Probe = """
        import {pathToFileURL} from 'node:url';
        const load = async name => import(pathToFileURL(name).href);
        const planner = await load(process.argv[2]);
        const scale = await load(process.argv[3]);
        const receiptModule = await load(process.argv[4]);
        const vectorsModule = await import(new URL('./vector-isolated-plan.mjs',pathToFileURL(process.argv[3])).href);
        const contract = planner.readIsolatedContract();
        const control = planner.createIsolatedPlan(contract);
        const scales = scale.createScaledPlans(contract);
        const vectors = vectorsModule.createVectorPlans(contract);
        const common = {sourceRevision:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',runId:37111111111,attempt:1,
          repository:'managedcode/KeyLoad',ref:'refs/heads/main',workflow:'Benchmarks'};
        const missingEvidence = ['effectiveServerResources','hardwareClass','serverCpuRss','storageEnvelope'];
        const missingResource = () => ({sha256:'e'.repeat(64),qualified:false,missingEvidence:[...missingEvidence],comparison:null});
        let next = 1;
        const proof = plan => ({schemaVersion:1,cohort:{...common,profile:plan.profile},cells:plan.cells.map(cell=>({
          id:cell.id,job:{id:next,name:'job',url:'job',conclusion:'success',steps:[]},
          artifact:{id:next++,name:'comparison-worker-'+cell.id,sizeInBytes:1,digest:'sha256:'+'b'.repeat(64),expired:false},
          workerSha256:'c'.repeat(64),serverResource:missingResource()}))});
        const proofs = [control,...scales,...vectors].map(proof);
        const manifests = [control,...scales,...vectors].map((plan,index)=>({profile:plan.profile,
          cohort:{...common,profile:plan.profile},workers:plan.cells.map((cell,cellIndex)=>({
          id:cell.id,target:cell.target,nodeCount:cell.nodeCount,scenario:cell.scenario,profile:cell.profile,
          disposition:contract.unsupportedTopologies.some(item=>item.target===cell.target&&item.nodeCounts.includes(cell.nodeCount))?'unsupportedTopology':'measured',
          reason:contract.unsupportedTopologies.find(item=>item.target===cell.target&&item.nodeCounts.includes(cell.nodeCount))?.reason??null,
          rawSha256:'c'.repeat(64)}))}));
        const vectorSupport={'PostgreSQL + pgvector':['Exact','Hnsw','IvfFlat'],Qdrant:['Exact','Hnsw'],SurrealDB:['Exact','Hnsw'],HelixDB:['NativeAnn']};
        for(let index=3;index<manifests.length;index++) {
          const settings=vectors[index-3].profileSettings;
          for(const worker of manifests[index].workers) {
            worker.vectorMetrics=null;
            if(worker.disposition==='unsupportedTopology') continue;
            if(worker.target==='KeyLoad') {
              worker.disposition='unsupported';
              worker.reason='KeyLoad SDK does not expose persisted vector readback or native numeric predicates required for scaled vector qualification.';
            } else if(!(vectorSupport[worker.target]??[]).includes(settings.indexKind)) {
              worker.disposition='unsupported';
              worker.reason=worker.target+' does not implement '+settings.indexKind+'/'+settings.queryMode+' natively.';
            } else {
              worker.disposition='failed';
              proofs[index].cells.find(item=>item.id===worker.id).job.conclusion='failure';
            }
          }
        }
        const vectorArguments={vectorPlans:vectors,vectorManifests:manifests.slice(3),vectorProofs:proofs.slice(3)};
        const failedId='keyload-n1-point-read-scaled-100k-c16';
        const failedWorker=manifests[1].workers.find(item=>item.id===failedId);
        failedWorker.disposition='failed';
        proofs[1].cells.find(item=>item.id===failedId).job.conclusion='failure';
        const result=receiptModule.createScaleCohortReceipt({control:manifests[0],controlProof:proofs[0],
          controlHash:'d'.repeat(64),plans:scales,manifests:manifests.slice(1,3),proofs:proofs.slice(1,3),...vectorArguments,contract});
        let rejectsDuplicateGlobalIdentity=false;
        const duplicateProof=structuredClone(proofs[1]);
        duplicateProof.cells[0].artifact.id=proofs[0].cells[0].artifact.id;
        try { receiptModule.createScaleCohortReceipt({control:manifests[0],controlProof:proofs[0],
          controlHash:'d'.repeat(64),plans:scales,manifests:manifests.slice(1,3),proofs:[duplicateProof,proofs[2]],...vectorArguments,contract}); }
        catch { rejectsDuplicateGlobalIdentity=true; }
        let rejectsTamper=false;
        proofs[1].cells[0].artifact.digest='invalid';
        try { receiptModule.createScaleCohortReceipt({control:manifests[0],controlProof:proofs[0],
          controlHash:'d'.repeat(64),plans:scales,manifests:manifests.slice(1,3),proofs:proofs.slice(1,3),...vectorArguments,contract}); }
        catch { rejectsTamper=true; }
        const allScaleCellsMissingResources=result.scaledProfiles.every(profile=>profile.cells.every(cell=>cell.resourceEquivalence===null));
        result.control.cells=[];
        result.scaledProfiles=result.scaledProfiles.map(profile=>({...profile,cells:profile.cells.filter(cell=>cell.target==='Neo4j'&&cell.nodeCount===3)}));
        result.vectorProfiles=result.vectorProfiles.map(({cells,...profile})=>profile);
        process.stdout.write(JSON.stringify({result,allScaleCellsMissingResources,rejectsTamper,rejectsDuplicateGlobalIdentity}));
        """;

    [Test]
    public async Task AcScale015ReceiptRetainsFailedIdsAndNeverClaimsAbsentServerEvidence()
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleMode, EvaluationMode, Probe, nameof(ScaledCohortReceiptTests), IsolatedAggregateNodeProcess.Module("isolated-plan.mjs"),
                IsolatedAggregateNodeProcess.Module("scaled-isolated-plan.mjs"),
                IsolatedAggregateNodeProcess.Module("scaled-cohort-receipt.mjs")],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0).Because(response.Error);
        using var result = JsonDocument.Parse(response.Output);
        var receipt = result.RootElement.GetProperty(IsolatedPlanFields.Result);
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.SchemaVersion).GetInt32()).IsEqualTo(1);
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.Control).GetProperty(IsolatedPlanFields.CellCount).GetInt32()).IsEqualTo(220);
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.ScaledProfiles).GetArrayLength()).IsEqualTo(2);
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.ScaledProfiles).EnumerateArray()
            .All(item => item.GetProperty(IsolatedPlanFields.CellCount).GetInt32() == 88)).IsTrue();
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.ScaledProfiles).EnumerateArray()
            .SelectMany(item => item.GetProperty(IsolatedPlanFields.Cells).EnumerateArray())
            .All(item => item.GetProperty(IsolatedPlanFields.ResourceEquivalence).ValueKind == JsonValueKind.Null)).IsTrue();
        var unsupported = receipt.GetProperty(IsolatedPlanFields.ScaledProfiles)[0].GetProperty(IsolatedPlanFields.Cells).EnumerateArray()
            .Where(item => item.GetProperty(IsolatedPlanFields.Target).GetString() == "Neo4j"
                && item.GetProperty(IsolatedPlanFields.NodeCount).GetInt32() == 3).ToArray();
        await Assert.That(unsupported.Length).IsEqualTo(4);
        string?[] expectedScenarios = ["PointRead", "DocumentWrite", "DocumentUpdate", "DocumentDelete"];
        await Assert.That(unsupported.Select(item => item.GetProperty(IsolatedPlanFields.Scenario).GetString()))
            .IsEquivalentTo(expectedScenarios);
        foreach (var cell in unsupported)
        {
            await Assert.That(cell.GetProperty(IsolatedPlanFields.Disposition).GetString()).IsEqualTo("unsupportedTopology");
            await Assert.That(cell.GetProperty(IsolatedPlanFields.ServerResourceQualified).GetBoolean()).IsFalse();
        }
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.Qualified).GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.VectorProfiles).GetArrayLength()).IsEqualTo(24);
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.VectorProfiles).EnumerateArray().All(profile => profile.GetProperty(IsolatedPlanFields.CellCount).GetInt32() == 22)).IsTrue();
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.FailedIds).EnumerateArray().Select(item => item.GetString())).Contains("keyload-n1-point-read-scaled-100k-c16");
        await Assert.That(receipt.GetProperty(IsolatedPlanFields.MissingEvidence).EnumerateArray().Select(item => item.GetString())
            .SequenceEqual(["effectiveServerResources", "hardwareClass", "serverCpuRss", "storageEnvelope"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(result.RootElement.GetProperty(IsolatedPlanFields.AllScaleCellsMissingResources).GetBoolean()).IsTrue();
        await Assert.That(result.RootElement.GetProperty(IsolatedPlanFields.RejectsTamper).GetBoolean()).IsTrue();
        await Assert.That(result.RootElement.GetProperty(IsolatedPlanFields.RejectsDuplicateGlobalIdentity).GetBoolean()).IsTrue();
    }
}
