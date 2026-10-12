using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteBm25CorpusPublication(IAtomicTransaction transaction,
        PrincipalRecord administrator, ReplicatedOperation operation, long position)
    {
        var phase = Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        RequireEnvelopeId(operation, phase.CommandId);
        var principal = RequireBm25Subject(transaction, administrator, phase, operation.EvaluatedAt);
        var current = phase.Current;
        if (operation.EvaluatedAt >= phase.OriginalExpiry)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Changed); }
        var resource = Resource(transaction, current.Partition, current.Collection, ResourceKind.Collection);
        var placement = ReadPlacementWitness(transaction, current.Partition);
        if (!Bm25CorpusDependencies.Matches(this, transaction, principal, resource, current,
            placement, Limits.MaxBatchBytes))
        { throw Errors.Fail(ErrorCode.Conflict, Bm25CorpusProtocol.Changed); }
        var key = Bm25CorpusKeys.Current(current);
        var previous = transaction.GetRecord<Bm25CorpusCurrentV1>(key);
        if (previous?.Generation != phase.ExpectedGeneration)
        { throw Errors.Fail(ErrorCode.Conflict, Bm25CorpusProtocol.Changed); }
        Bm25CorpusFamilyAdmission.Require(transaction, key, current, Limits);
        transaction.PutRecord(key, current);
        return Result(new CommitReceipt(operation.Id, Token(transaction, current.Partition, position), [], Durability));
    }
}
