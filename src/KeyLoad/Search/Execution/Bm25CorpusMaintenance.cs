using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class Bm25CorpusMaintenance
{
    internal static async Task<CommitReceipt> PublishAsync(OrleansNode node, PartitionHost partition,
        string administratorId, string subjectId, SearchRequest query, Guid commandId,
        Guid? expectedGeneration, DateTimeOffset originalExpiry, ReadExecutionBudget work,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        work.Check();
        work.ConstrainLifetime(originalExpiry);
        await ReadCurrentViewAsync(partition, originalExpiry, work, token);
        work.Check();
        var operation = Bm25CorpusCapture.Create(partition.Database, administratorId, subjectId,
            query, commandId, expectedGeneration, originalExpiry, work,
            node.RuntimeServices.GetRequiredService<IOptions<QueryExecutionOptions>>());
        return await PublishAsync(node, partition, operation, originalExpiry, work, token);
    }

    internal static async Task<CommitReceipt> PublishAsync(OrleansNode node, PartitionHost partition,
        Command<DatabaseCommand> original, DateTimeOffset envelopeExpiry, ReadExecutionBudget work,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        token.ThrowIfCancellationRequested();
        work.ConstrainLifetime(envelopeExpiry);
        var verified = partition.Database.OwnCommand(original);
        if (verified.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
        }
        var phase = OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>(verified.Value!.Input.Span);
        var actual = partition.Database.CreateVerifiedBm25CorpusPublication(verified.UserId!, phase, work);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(
            work.CreateView(view), actual.UserId!, partition.Database.EvaluationClock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var envelope = node.CatalogRequestCodec().CreateBm25CorpusPublication(requestId, actual, envelopeExpiry);
        return await Bm25CorpusCall.ExecuteAsync(node, partition, principal, requestId,
            actual.CommandId, envelope, work, token);
    }

    private static async Task ReadCurrentViewAsync(PartitionHost partition, DateTimeOffset originalExpiry,
        ReadExecutionBudget work, CancellationToken token)
    {
        var clock = partition.Database.EvaluationClock;
        var remaining = originalExpiry - clock.GetUtcNow();
        var lifetime = work.RemainingLifetime;
        var bounded = remaining < lifetime ? remaining : lifetime;
        if (bounded <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Changed);
        }
        var failures = new List<Exception>();
        try
        {
            using var deadline = new CancellationTokenSource(bounded, clock);
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(token,
                work.Cancellation, deadline.Token);
            await ServerFailureObserver.ObserveAsync(() =>
                partition.Coordinator.ReadBarrierAsync(caller.Token), failures);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        work.Check();
    }
}
