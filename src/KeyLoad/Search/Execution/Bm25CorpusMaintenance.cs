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
        Guid? expectedGeneration, ReadExecutionBudget work,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        work.Check();
        await ReadCurrentViewAsync(partition, work, token);
        work.Check();
        var operation = Bm25CorpusCapture.Create(partition.Database, administratorId, subjectId,
            query, commandId, expectedGeneration, work,
            node.RuntimeServices.GetRequiredService<IOptions<QueryExecutionOptions>>());
        return await PublishAsync(node, partition, operation, work, token);
    }

    internal static async Task<CommitReceipt> PublishAsync(OrleansNode node, PartitionHost partition,
        Command<DatabaseCommand> original, ReadExecutionBudget work,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        token.ThrowIfCancellationRequested();
        var verified = partition.Database.OwnCommand(original);
        if (verified.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Bm25CorpusProtocol.Administrator);
        }
        var phase = OperationProtocol.Payload<Bm25CorpusPublicationPhaseCommand>(verified);
        var actual = partition.Database.CreateVerifiedBm25CorpusPublication(verified.UserId!, phase, work);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(
            work.CreateView(view), actual.UserId!, partition.Database.EvaluationClock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var envelope = node.CatalogRequestCodec().CreateBm25CorpusPublication(requestId, actual);
        return await Bm25CorpusCall.ExecuteAsync(node, principal, requestId,
            actual.CommandId, envelope, work, token);
    }

    private static async Task ReadCurrentViewAsync(PartitionHost partition,
        ReadExecutionBudget work, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, work.Cancellation);
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
