using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceFlow
{
    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var options = UnitExecutionOptions.NativeMovementProcess().Value;
        using var deadline = new CancellationTokenSource(options.OperationTimeout, source.Database.EvaluationClock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var token = original.Token;
        var state = await ResourceRetirementQuiescenceSeed.ExecuteAsync(source, target, listeners, corpus, token);
        try
        {
            var phase = await ResourceRetirementQuiescencePhases.SubmitAsync(state, source,
                ResourceRetirementPhase.PrepareRetirement, ResourceRetirementIdentity.PreparationCommandId(state.Request),
                new ResourceRetirementPrepareBodyV1(state.Request), token);
            state.Control = phase.PreparationState?.Control ?? throw new InvalidOperationException(Invalid);
            await Assert.That(state.Control.PrincipalId).IsEqualTo(Root);
            await Assert.That(state.Control.PolicyEpoch).IsEqualTo(ResourceRetirementQuiescencePhases.Epoch(source));
            await ResourceRetirementQuiescenceAssertions.EqualAsync(new ResourceDefinition(
                ControlledPartitionMovementCorpus.Collection, ResourceKind.Collection, Partition.TransactionDomainId), state.Control.Definition);
            await ResourceRetirementQuiescenceAssertions.EqualAsync(phase.Receipt, state.Control.PrepareReceipt);
            await Assert.That(state.Control.IntentDigest).IsEqualTo(phase.Receipt.IntentDigest);
            await ResourceRetirementQuiescenceAssertions.EqualAsync(new ResourceRetirementAcceptedMoveV1(Partition,
                ControlledPartitionMovementPrepareRequest.MoveId), state.Control.SourceAcceptedMoves.Single());
            await ResourceRetirementQuiescenceAssertions.EqualAsync(state.Request, state.Control.Request);
            await ResourceRetirementQuiescenceAssertions.EqualAsync(ResourceRetirementQuiescencePhases.ReadLifetime(source),
                state.Control.Lifetime);
            await ResourceRetirementQuiescenceReservation.ReserveAsync(state, token);
            await ResourceRetirementQuiescenceStatuses.ReadAsync(state, settled: false, token);
            await ResourceRetirementQuiescenceAdmissions.RunAsync(source, target, corpus,
                (sourceRuntime, targetRuntime, sourceAdmission, targetAdmission) =>
                    ResourceRetirementQuiescenceNewMove.RefuseAsync(state, sourceRuntime, sourceAdmission, token));
            await ResourceRetirementQuiescenceCold.ReopenAsync(state, settled: false, token);
            await AbortAsync(state, token);
            await ResourceRetirementQuiescenceStatuses.IncompleteBeginAsync(state, token);
            await ResourceRetirementQuiescenceBusiness.WriteAsync(state, Zero, token);
            await ResourceRetirementQuiescenceCold.ReopenAsync(state, settled: true, token);
            await ResourceRetirementQuiescenceStatuses.IncompleteBeginAsync(state, token);
            await ResourceRetirementQuiescenceBusiness.WriteAsync(state, One, token);
            await ResourceRetirementQuiescenceAssertions.ReplayAsync(state, token);
            await ResourceRetirementQuiescenceAssertions.ModelsAsync(state, token);
        }
        catch (Exception failure)
        {
            failure.Data[ExpectedFailuresKey] = state.ExpectedFailures.ToArray();
            failure.Data[OriginalsKey] = state.Originals.ToArray();
            failure.Data[SourceOwnerKey] = state.Source;
            failure.Data[TargetOwnerKey] = state.Target;
            throw;
        }
    }

    private static async Task AbortAsync(ResourceRetirementQuiescenceState state, CancellationToken token)
    {
        await ResourceRetirementQuiescenceAdmissions.RunAsync(state.Source, state.Target, state.Corpus,
            async (sourceRuntime, targetRuntime, sourceAdmission, targetAdmission) =>
            {
                var completed = await ControlledPartitionMovementActiveAbort.ExecuteAsync(state.Source, state.Target,
                    sourceRuntime, targetRuntime, sourceAdmission, targetAdmission, state.Corpus, state.Accepted,
                    ControlledPartitionMovementPrepareRequest.CallerAddress(state.Listeners), state.ExpiresAt, token);
                await Assert.That(completed.Control!.Phase).IsEqualTo(PartitionMovePhase.Aborted);
                var original = state.Source.Journal.Log.Read(completed.Journal.AppliedPosition, One,
                    state.Source.Journal.Configuration.MaxAppendBytes).Single().Operation ?? throw new InvalidOperationException(Invalid);
                var result = state.Source.Database.ResolveOutcome(original);
                await Assert.That(result.Error).IsNull();
                await ResourceRetirementQuiescenceAssertions.EqualAsync(completed, result.Get<PartitionMovePhaseResult>());
                state.Originals.Add((state.Source, original, result));
            });
        await ResourceRetirementQuiescenceStatuses.ReadAsync(state, settled: true, token);
        await ResourceRetirementQuiescenceAssertions.ModelsAsync(state, token);
    }
}
