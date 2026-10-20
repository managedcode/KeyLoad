using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageValidation operations against the caller's database context.</summary>
public static class DocumentStorageValidation
{
    internal const string DocumentTokenWrongIncarnation = "The document session token belongs to another incarnation.";
    internal const string DocumentTokenOutOfScope = "The document session token belongs to another atomic partition.";
    internal const string DocumentTokenInvalidPosition = "The document session token position must be positive.";
    internal const string DocumentTokenFuturePosition = "The document session token is beyond the current committed storage cut.";
    internal const string DocumentTokenCorruptPosition = "The canonical applied position is invalid.";
    internal const long DocumentTokenInitialPosition = 0;
    internal static void ValidateDocumentSessionToken(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, CommitToken token)
    {
        var owner = engine.ReadPlacementWitness(view, partition);
        if (token.Incarnation != owner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenWrongIncarnation);
        }

        if (token.AtomicPartitionId != partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenOutOfScope);
        }

        if (token.Position <= DocumentTokenInitialPosition)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenInvalidPosition);
        }

        var bytes = view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
        var applied = OrleansSerialization.Deserialize<long>(bytes);
        if (applied < DocumentTokenInitialPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
        }

        if (token.Position > applied)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenFuturePosition);
        }
    }
}
