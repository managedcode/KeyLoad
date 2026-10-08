using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Runs a real protected replacement through both native owners and replays its original A outcome.</summary>
internal static class ControlledDocumentNativeCommandOperation
{
    internal const string UpdatedJson = "{\"state\":\"derived\",\"text\":\"Київ knowledge\"}";
    internal const long UpdatedRevision = 2;
    private static readonly Guid OriginalId = Guid.Parse("bc107e1c-4857-4a9b-aeb8-c218d2a22156");

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions sourceOptions,
        ServerRuntimeOptions targetOptions, PartitionMovementPeerAdmission sourceAdmission,
        PartitionMovementPeerAdmission targetAdmission, CancellationToken cancellationToken = default)
    {
        var token = cancellationToken.CanBeCanceled ? cancellationToken : TestContext.Current!.Execution.CancellationToken;
        var request = new CommandRequest(OriginalId, ControlledPartitionMovementCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId,
                UpdatedJson, ControlledPartitionMovementCorpus.InitialRevision, ExplicitReplacement: true)]);
        var original = source.Database.CreateNativeOperation(OperationKind.Batch, OriginalId,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
        var work = new ReadExecutionBudget(Options.Create(source.Database.Limits),
            source.Database.EvaluationClock, token);
        var context = source.Database.TryCaptureControlledDocumentCommand(original.PrincipalId, original, work)
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var expiry = source.Database.EvaluationClock.GetUtcNow() + sourceOptions.GrainRouting.Value.RequestLifetime;
        var phases = new ControlledDocumentNativePhaseFlow(source, target, corpus, sourceOptions, targetOptions,
            sourceAdmission, targetAdmission, ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), work);
        var targetIndex = target.Journal.Log.State.LastIndex;
        var effect = await ControlledDocumentNativeCommandStages.ExecuteAsync(phases, original, context, expiry, token);
        await ControlledDocumentNativeCommandAssertions.CompleteAsync(source, target, original, context,
            effect, targetIndex, token);
    }
}
