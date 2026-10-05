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
        const planner = await load(process.argv[1]);
        const scale = await load(process.argv[2]);
        const receiptModule = await load(process.argv[3]);
        const contract = planner.readIsolatedContract();
        const control = planner.createIsolatedPlan(contract);
        const scales = scale.createScaledPlans(contract);
        const common = {sourceRevision:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',runId:37111111111,attempt:1,
          repository:'managedcode/KeyLoad',ref:'refs/heads/main',workflow:'Benchmarks'};
        const missingEvidence = ['effectiveServerResources','hardwareClass','serverCpuRss','storageEnvelope'];
        const missingResource = () => ({sha256:'e'.repeat(64),qualified:false,missingEvidence:[...missingEvidence],comparison:null});
        let next = 1;
        const proof = plan => ({schemaVersion:1,cohort:{...common,profile:plan.profile},cells:plan.cells.map(cell=>({
          id:cell.id,job:{id:next,name:'job',url:'job',conclusion:'success',steps:[]},
          artifact:{id:next++,name:'comparison-worker-'+cell.id,sizeInBytes:1,digest:'sha256:'+'b'.repeat(64),expired:false},
          workerSha256:'c'.repeat(64),serverResource:missingResource()}))});
        const proofs = [control,...scales].map(proof);
        const manifests = [control,...scales].map((plan,index)=>({profile:plan.profile,
          cohort:{...common,profile:plan.profile},workers:plan.cells.map((cell,cellIndex)=>({
          id:cell.id,target:cell.target,nodeCount:cell.nodeCount,scenario:cell.scenario,profile:cell.profile,
          disposition:cell.target==='Neo4j' && cell.nodeCount>1?'unsupportedTopology':'measured',
          reason:cell.target==='Neo4j' && cell.nodeCount>1?contract.unsupportedTopologies[0].reason:null,
          rawSha256:'c'.repeat(64)}))}));
        const failedId='keyload-n1-point-read-scaled-100k-c16';
        const failedWorker=manifests[1].workers.find(item=>item.id===failedId);
        failedWorker.disposition='failed';
        proofs[1].cells.find(item=>item.id===failedId).job.conclusion='failure';
        const result=receiptModule.createScaleCohortReceipt({control:manifests[0],controlProof:proofs[0],
          controlHash:'d'.repeat(64),plans:scales,manifests:manifests.slice(1),proofs:proofs.slice(1),contract});
        let rejectsDuplicateGlobalIdentity=false;
        const duplicateProof=structuredClone(proofs[1]);
        duplicateProof.cells[0].artifact.id=proofs[0].cells[0].artifact.id;
        try { receiptModule.createScaleCohortReceipt({control:manifests[0],controlProof:proofs[0],
          controlHash:'d'.repeat(64),plans:scales,manifests:manifests.slice(1),proofs:[duplicateProof,...proofs.slice(2)],contract}); }
        catch { rejectsDuplicateGlobalIdentity=true; }
        let rejectsTamper=false;
        proofs[1].cells[0].artifact.digest='invalid';
        try { receiptModule.createScaleCohortReceipt({control:manifests[0],controlProof:proofs[0],
          controlHash:'d'.repeat(64),plans:scales,manifests:manifests.slice(1),proofs:proofs.slice(1),contract}); }
        catch { rejectsTamper=true; }
        process.stdout.write(JSON.stringify({result,rejectsTamper,rejectsDuplicateGlobalIdentity}));
        """;

    [Test]
    public async Task AcScale015ReceiptRetainsFailedIdsAndNeverClaimsAbsentServerEvidence()
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(
            [ModuleMode, EvaluationMode, Probe, IsolatedAggregateNodeProcess.Module("isolated-plan.mjs"),
                IsolatedAggregateNodeProcess.Module("scaled-isolated-plan.mjs"),
                IsolatedAggregateNodeProcess.Module("scaled-cohort-receipt.mjs")],
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0).Because(response.Error);
        using var result = JsonDocument.Parse(response.Output);
        var receipt = result.RootElement.GetProperty("result");
        await Assert.That(receipt.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
        await Assert.That(receipt.GetProperty("control").GetProperty("cellCount").GetInt32()).IsEqualTo(270);
        await Assert.That(receipt.GetProperty("scaledProfiles").GetArrayLength()).IsEqualTo(3);
        await Assert.That(receipt.GetProperty("scaledProfiles").EnumerateArray()
            .All(item => item.GetProperty("cellCount").GetInt32() == 108)).IsTrue();
        await Assert.That(receipt.GetProperty("scaledProfiles").EnumerateArray()
            .SelectMany(item => item.GetProperty("cells").EnumerateArray())
            .All(item => item.GetProperty("resourceEquivalence").ValueKind == JsonValueKind.Null)).IsTrue();
        var unsupported = receipt.GetProperty("scaledProfiles")[0].GetProperty("cells").EnumerateArray()
            .Single(item => item.GetProperty("target").GetString() == "Neo4j"
                && item.GetProperty("nodeCount").GetInt32() == 2);
        await Assert.That(unsupported.GetProperty("disposition").GetString()).IsEqualTo("unsupportedTopology");
        await Assert.That(unsupported.GetProperty("serverResourceQualified").GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty("qualified").GetBoolean()).IsFalse();
        await Assert.That(receipt.GetProperty("failedIds")[0].GetString()).IsEqualTo("keyload-n1-point-read-scaled-100k-c16");
        await Assert.That(receipt.GetProperty("missingEvidence").EnumerateArray().Select(item => item.GetString())
            .SequenceEqual(["effectiveServerResources", "hardwareClass", "serverCpuRss", "storageEnvelope"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(result.RootElement.GetProperty("rejectsTamper").GetBoolean()).IsTrue();
        await Assert.That(result.RootElement.GetProperty("rejectsDuplicateGlobalIdentity").GetBoolean()).IsTrue();
    }
}
