using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class ControlledBlobOutcomeLifetimeNativeFlow
{
    private const int KeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken token)
    {
        await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
        await ConfigureTargetAsync(target, token);
        var sourceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
        var targetKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
        var sourceOptions = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus, sourceKey, targetKey, true);
        var targetOptions = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus, sourceKey, targetKey, false);
        var execution = new ControlledBlobOutcomeLifetimeNativeExecution(source, target, listeners, corpus,
            sourceOptions, targetOptions);
        var proofs = await ControlledBlobOutcomeLifetimeNativeCommands.SeedAsync(execution, token);
        var state = new ControlledBlobOutcomeLifetimeNativeState(source, target, proofs, corpus.Destination,
            ControlledPartitionMovementPrepareRequest.CallerAddress(listeners));
        await ControlledBlobOutcomeLifetimeNativeAssertions.InventoryAsync(state);
        await ControlledBlobOutcomeLifetimeNativeCold.ExecuteAsync(state, token);
        await ControlledBlobOutcomeLifetimeNativeRefusals.ExecuteAsync(state, execution, token);
    }

    internal static async Task<PartitionControlEffectPayload> ApplyAsync(ControlledBlobOutcomeLifetimeNativeExecution execution,
        ReplicatedOperation original, CancellationToken token)
    {
        var failures = new List<Exception>();
        PartitionControlEffectPayload? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceAdmission = Open(execution.Source, execution.SourceOptions);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var targetAdmission = Open(execution.Target, execution.TargetOptions);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    result = await ApplyAtCurrentOwnersAsync(execution, original, sourceAdmission, targetAdmission, token);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
    }

    private static async Task<PartitionControlEffectPayload> ApplyAtCurrentOwnersAsync(
        ControlledBlobOutcomeLifetimeNativeExecution execution, ReplicatedOperation original,
        PartitionMovementPeerAdmission sourceAdmission, PartitionMovementPeerAdmission targetAdmission, CancellationToken token)
    {
        var source = execution.Source;
        var work = ControlledBlobOutcomeLifetimeNativeReads.Work(source, token);
        var context = source.Database.TryCaptureControlledDocumentCommand(original.PrincipalId, original, work)
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var phases = new ControlledDocumentNativePhaseFlow(source, execution.Target, execution.Corpus,
            execution.SourceOptions, execution.TargetOptions, sourceAdmission, targetAdmission,
            ControlledPartitionMovementPrepareRequest.CallerAddress(execution.Listeners), work);
        return await ControlledDocumentNativeCommandStages.ExecuteAsync(phases, original, context,
            source.Database.EvaluationClock.GetUtcNow() + execution.SourceOptions.GrainRouting.Value.RequestLifetime, token);
    }

    private static PartitionMovementPeerAdmission Open(ControlledPartitionMovementNode node, ServerRuntimeOptions options)
        => new(options.Node, node.Database, options.ReplicaConfiguration, options.GrainRouting, options.Membership,
            options.PartitionMovement, node.Database.EvaluationClock);

    private static async Task ConfigureTargetAsync(ControlledPartitionMovementNode target, CancellationToken token)
    {
        var principal = target.Store.Read(view => target.Database.Principal(view, "root", target.Database.EvaluationClock.GetUtcNow()));
        var differentEpoch = principal with
        {
            PolicyEpoch = checked(principal.PolicyEpoch + 1)
        };
        var original = target.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), "root",
            target.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(differentEpoch)));
        await Assert.That(target.Journal.Submit(original, token).Error).IsNull();
    }
}
