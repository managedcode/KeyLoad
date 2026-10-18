using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long NoBm25ReceiptPosition = 0;

    private void ValidateBm25CachedOutcome(IKeyValueView view, PrincipalRecord administrator,
        Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (operation.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase)
        {
            return;
        }
        var phase = Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        RequireBm25Subject(view, administrator, phase, operation.Timestamp);
        if (Errors.Code(previous.Result.Problem) is not null)
        {
            return;
        }
        var receipt = previous.Result.Get<CommitReceipt>();
        if (receipt is null || receipt.CommandId != phase.CommandId || receipt.Token is null
            || receipt.Token.AtomicPartitionId != phase.Current.Partition.AtomicPartitionId
            || receipt.Token.Incarnation != phase.Current.Incarnation || receipt.Token.Position <= NoBm25ReceiptPosition
            || receipt.Mutations.IsDefault || !receipt.Mutations.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
    }
}
