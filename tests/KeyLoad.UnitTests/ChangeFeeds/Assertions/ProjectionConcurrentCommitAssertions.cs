using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.ChangeFeeds;

internal static class ProjectionConcurrentCommitAssertions
{
    internal const string Effect = "effect";
    internal const string Alternative = "alternative";
    internal const string Json = "{\"sourceCount\":2}";
    private const string ConflictDetail = "The projection batch was completed with different effects.";

    internal static async Task<bool> OutcomesAsync(Command<DatabaseCommand> first, Result<object> a,
        Command<DatabaseCommand> second, Result<object> b, bool changed)
    {
        bool winnerIsFirst;
        if (changed)
        {
            await Assert.That((Errors.Code(a.Problem) is null) != (Errors.Code(b.Problem) is null)).IsTrue();
            winnerIsFirst = Errors.Code(a.Problem) is null;
            await ProjectionConsumerIdentityAssertions.ExactAsync(winnerIsFirst ? b : a,
                Result<object>.Fail(Errors.Problem(ErrorCode.Conflict, ConflictDetail)));
        }
        else
        {
            await Assert.That(Errors.Code(a.Problem)).IsNull();
            await Assert.That(Errors.Code(b.Problem)).IsNull();
            await Assert.That(a.Get<ProjectionBatchResult>().AlreadyProcessed != b.Get<ProjectionBatchResult>().AlreadyProcessed).IsTrue();
            winnerIsFirst = !a.Get<ProjectionBatchResult>().AlreadyProcessed;
        }
        var winningOperation = winnerIsFirst ? first : second;
        var winner = (winnerIsFirst ? a : b).Get<ProjectionBatchResult>();
        await ProjectionConsumerIdentityAssertions.ReceiptAsync(winningOperation, winner.Receipt,
            new MutationReceipt("putDocument", ProjectionConsumerIdentityFixture.Target,
                winnerIsFirst || !changed ? Effect : Alternative, 1L));
        await ProjectionConsumerIdentityAssertions.ExactAsync(winner, new ProjectionBatchResult(winner.Receipt, false, 2L));
        if (!changed)
        {
            await ProjectionConsumerIdentityAssertions.ExactAsync((winnerIsFirst ? b : a).Get<ProjectionBatchResult>(),
                new ProjectionBatchResult(winner.Receipt, true, 2L));
        }
        return winnerIsFirst;
    }

    internal static async Task DocumentsAsync(DatabaseEngine owner, ProjectionConsumerIdentityState source,
        bool winnerIsFirst, bool changed, CancellationToken token)
    {
        await ProjectionConsumerIdentityAssertions.SourcesAsync(owner, source);
        var id = winnerIsFirst || !changed ? Effect : Alternative;
        var absent = id == Effect ? Alternative : Effect;
        await ProjectionConsumerIdentityAssertions.ExactAsync(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(source.Partition, ProjectionConsumerIdentityFixture.Target, id), cancellationToken: token),
            new DocumentResult(source.Creation.ExpectedReference(new(source.Partition, ProjectionConsumerIdentityFixture.Target, id)), 1L, Json, false, []));
        await Assert.That(owner.GetDocument(ProjectionConsumerIdentityFixture.Root,
            new(source.Partition, ProjectionConsumerIdentityFixture.Target, absent), cancellationToken: token)).IsNull();
    }
}
