using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-VQ-007: native capability and failed-worker admission preserve null numerical data.</summary>
internal sealed class VectorAggregateAdmissionTests
{
    private const string AcceptedKey = "accepted";
    private const string RejectsInventedReasonKey = "rejectsInventedReason";
    private const string RejectsInventedMeasurementKey = "rejectsInventedMeasurement";
    private const string AcceptsFailedKey = "acceptsFailed";
    private const string RejectsMixedProfileKey = "rejectsMixedProfile";
    private const string AcceptsHelixUnsupportedKey = "acceptsHelixUnsupported";
    private const string AcceptsSurrealTopologyKey = "acceptsSurrealTopology";
    private const string RejectsHelixSyntheticKey = "rejectsHelixSynthetic";
    private const string Probe = """
        import {pathToFileURL} from 'node:url';
        const load=async file=>import(pathToFileURL(file).href);
        const {readIsolatedContract}=await load(process.argv[2]);
        const {createVectorPlans}=await load(process.argv[3]);
        const {validateWorkerEnvelope}=await load(process.argv[4]);
        const plan=createVectorPlans()[0];
        const contract={...readIsolatedContract(),profile:plan.profile,options:plan.profileSettings};
        const cohort={sourceRevision:'a'.repeat(40),runId:37,attempt:1,repository:'managedcode/KeyLoad',ref:'refs/heads/main',workflow:'Benchmarks',profile:plan.profile};
        const cell=plan.cells.find(cell=>cell.target==='KeyLoad'&&cell.nodeCount===1);
        const envelope={schemaVersion:5,worker:{...cohort,target:cell.target,nodeCount:1,scenario:'VectorExact',jobId:2},
          disposition:'unsupported',reason:'KeyLoad SDK does not expose persisted vector readback or native numeric predicates required for scaled vector qualification.',report:null};
        const accepts=value=>{try{validateWorkerEnvelope(value,cell,cohort,contract);return true;}catch{return false;}};
        const accepted=accepts(envelope);
        const rejectsInventedReason=!accepts({...envelope,reason:'No native vector support'});
        const rejectsInventedMeasurement=!accepts({...envelope,report:{value:10}});
        const acceptsFailed=accepts({...envelope,disposition:'failed',reason:'Benchmark failed; no measurement data is available.'});
        const rejectsMixedProfile=!accepts({...envelope,worker:{...envelope.worker,profile:'vector-1m-exact-plain-c16'}});
        const helix=plan.cells.find(cell=>cell.target==='HelixDB'&&cell.nodeCount===1);
        const helixEnvelope={...envelope,worker:{...envelope.worker,target:'HelixDB'},
          reason:'HelixDB does not implement Exact/Plain natively.'};
        const acceptsHelixUnsupported=(()=>{try{validateWorkerEnvelope(helixEnvelope,helix,cohort,contract);return true;}catch{return false;}})();
        const surreal=plan.cells.find(cell=>cell.target==='SurrealDB'&&cell.nodeCount===2);
        const topology=contract.unsupportedTopologies.find(item=>item.target==='SurrealDB');
        const surrealEnvelope={...envelope,worker:{...envelope.worker,target:'SurrealDB',nodeCount:2},
          disposition:'unsupportedTopology',reason:topology.reason};
        const acceptsSurrealTopology=(()=>{try{validateWorkerEnvelope(surrealEnvelope,surreal,cohort,contract);return true;}catch{return false;}})();
        const rejectsHelixSynthetic=(()=>{try{validateWorkerEnvelope({...helixEnvelope,disposition:'measured',reason:null,report:{recall:1}},helix,cohort,contract);return false;}catch{return true;}})();
        process.stdout.write(JSON.stringify({accepted,rejectsInventedReason,rejectsInventedMeasurement,acceptsFailed,
          rejectsMixedProfile,acceptsHelixUnsupported,acceptsSurrealTopology,rejectsHelixSynthetic}));
        """;

    [Test]
    public async Task KeyLoadMissingNativeReadbackCannotBecomeAScaleVectorMeasurement()
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(["--input-type=module", "-e", Probe, nameof(VectorAggregateAdmissionTests),
            IsolatedAggregateNodeProcess.Module("isolated-plan.mjs"),
            IsolatedAggregateNodeProcess.Module("vector-isolated-plan.mjs"),
            IsolatedAggregateNodeProcess.Module("aggregate-validation.mjs")], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0).Because(response.Error);
        using var document = JsonDocument.Parse(response.Output);
        string[] expectedKeys = [AcceptedKey, RejectsInventedReasonKey, RejectsInventedMeasurementKey, AcceptsFailedKey, RejectsMixedProfileKey, AcceptsHelixUnsupportedKey, AcceptsSurrealTopologyKey, RejectsHelixSyntheticKey];
        await Assert.That(document.RootElement.EnumerateObject().Select(item => item.Name))
            .IsEquivalentTo(expectedKeys);
        await Assert.That(expectedKeys.All(key => document.RootElement.GetProperty(key).GetBoolean())).IsTrue();
    }
}
