using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.ResourceExecution;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;
using R = KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionPrefixPrepareClock
{
    internal static Task RunAsync(bool budget, CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(null, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            await CompositionPrefixPrepareSeed.VerticesAsync(owner);
            foreach (var id in budget ? new[] { D.First, D.Second } : new[] { D.Message })
            {
                await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new EnqueueMessage(D.Queue, id, budget ? CompositionPrefixPrepareScenarios.Link(owner) : D.EmptyJson)],
                    [new(D.Enqueue, D.Queue, id, D.FirstRevision)]);
            }
            var image = CompositionPrefixPrepareAssertions.ModelImage(owner);
            // UnixEpoch is an actual signed old command time, matching the existing native marker regression.
            var stale = CompositionPrefixPrepareCommands.Issue(owner,
                [new PutDocument(D.From, D.Marker, D.MarkerJson, D.Absent),
                 CompositionPrefixPrepareCommands.Forward(typed: false)], evaluatedAt: DateTimeOffset.UnixEpoch);
            stale = stale with
            {
                Error = ErrorCode.ClockUncertain,
                SafeDetail = D.ClockBehind
            };
            await CompositionPrefixPrepareAssertions.ResultAsync(stale, owner.Submit(stale.Original));
            owner.Commands.Add(stale);
            await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            var fresh = CompositionPrefixPrepareCommands.Issue(owner,
                [new PutDocument(D.From, D.Marker, D.MarkerJson, D.Absent),
                 CompositionPrefixPrepareCommands.Forward(typed: false)]);
            fresh = fresh with
            {
                Error = budget ? ErrorCode.BudgetExceeded : ErrorCode.Validation,
                SafeDetail = budget ? D.SourceOverflow : D.InvalidLink
            };
            await CompositionPrefixPrepareAssertions.ResultAsync(fresh, owner.Submit(fresh.Original));
            owner.Commands.Add(fresh);
            await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
            {
                await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            });
        }, token);

    // Existing six independently owned local native stores, not RF3 or remote-voter qualification.
    internal static Task RetirementAsync() => ResourceRetirementPreparationValidationFixture.RunAsync(async (state, token) =>
    {
        await ResourceRetirementAttemptAdmissionFlow.ExecuteAsync(state, token);
        var source = state.Nodes[R.SourceIndex];
        var current = source.Database.ReadResourceRetirementState(D.Root, state.Preparation.Control.Request,
            ResourceRetirementPreparationValidationFixture.Work(source, token));
        await Assert.That(current.Stage).IsEqualTo(ResourceRetirementStage.Retiring);
        var partition = ControlledPartitionMovementCorpus.Partition;
        var request = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(current.Control.Lifetime.ResourceName, D.Marker, D.MarkerJson, D.Absent)]);
        var work = ResourceRetirementPreparationValidationFixture.Work(source, token);
        var original = source.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId, D.Root,
            DateTimeOffset.UnixEpoch, NativeSerialization.Serialize(request), work);
        var bytes = NativeSerialization.Serialize(original);
        var outcome = source.Journal.Submit(original, token);
        await Assert.That(outcome.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(outcome.SafeDetail).IsEqualTo(D.Retiring);
        await Assert.That(outcome.NativeValue).IsNull();
        await Assert.That(outcome.Json).IsNull();
        await Assert.That(NativeSerialization.Serialize(original).SequenceEqual(bytes)).IsTrue();
        var stored = OutcomeStoreOracle.ReadStored(source.Store, original)!;
        await Assert.That(stored.Result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(stored.Result.SafeDetail).IsEqualTo(D.Retiring);
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var index = source.Journal.Log.State.LastIndex;
        await CompositionPrefixPrepareAssertions.EqualAsync(outcome, source.Journal.Submit(original, token));
        await Assert.That(QueueWholeFlowStorage.Bytes(source.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
    });
}
