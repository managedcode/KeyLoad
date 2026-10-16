using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionPrefixPrepareRefusals
{
    internal static async Task LateConflictAsync(CompositionPrefixPrepareOwner owner)
    {
        var command = CompositionPrefixPrepareCommands.Issue(owner,
            [CompositionPrefixPrepareCommands.Forward(prefix: D.Rollback),
             CompositionPrefixPrepareCommands.Forward(prefix: D.Rollback)]);
        var before = CompositionPrefixPrepareAssertions.ModelImage(owner);
        var charged = command.Work.ReadBytes;
        var result = owner.Submit(command.Original);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(result.SafeDetail).IsEqualTo("The expected revision does not match.");
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(result.Json).IsNull();
        command = command with
        {
            Error = ErrorCode.RevisionConflict,
            SafeDetail = "The expected revision does not match."
        };
        owner.Commands.Add(command);
        await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        await Assert.That(command.Work.ReadBytes).IsEqualTo(charged);
        await CompositionPrefixPrepareAssertions.ReplayAsync(owner);
    }

    internal static Task CoarseAuthorizationAsync(CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(new()
        {
            MaxBatchMutations = D.EffectCap
        }, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            await CompositionPrefixPrepareSeed.VerticesAsync(owner);
            foreach (var id in new[] { D.First, D.Second })
            {
                await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new EnqueueMessage(D.Queue, id, CompositionPrefixPrepareScenarios.Link(owner))],
                    [new(D.Enqueue, D.Queue, id, D.FirstRevision)]);
            }
            var principal = new PrincipalRecord(D.Reader, owner.Source.Partition.TenantId, [], []);
            var configured = owner.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), D.Root,
                owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
            await CompositionPrefixPrepareAssertions.EqualAsync(principal, owner.Submit(configured).Get<PrincipalRecord>());
            var denied = CompositionPrefixPrepareCommands.Issue(owner, [CompositionPrefixPrepareCommands.Forward()], D.Reader);
            var image = CompositionPrefixPrepareAssertions.ModelImage(owner);
            var outcome = owner.Submit(denied.Original);
            await Assert.That(outcome.Error).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(outcome.SafeDetail).IsEqualTo("The principal cannot perform this operation in this scope.");
            denied = denied with
            {
                Error = ErrorCode.PermissionDenied,
                SafeDetail = "The principal cannot perform this operation in this scope."
            };
            owner.Commands.Add(denied);
            var bounded = CompositionPrefixPrepareCommands.Issue(owner, [CompositionPrefixPrepareCommands.Forward()]);
            var overflow = owner.Submit(bounded.Original);
            await Assert.That(overflow.Error).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(overflow.SafeDetail).IsEqualTo(D.SourceOverflow);
            owner.Commands.Add(bounded with
            {
                Error = ErrorCode.BudgetExceeded,
                SafeDetail = D.SourceOverflow
            });
            await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
            {
                await Assert.That(owner.Database.Traverse(D.Root, owner.Source.Partition, D.Graph,
                    CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First)).Edges).IsEmpty();
            });
        }, token);
}
