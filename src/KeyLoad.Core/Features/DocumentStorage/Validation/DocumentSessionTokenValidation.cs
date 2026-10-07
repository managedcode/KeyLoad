using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string DocumentTokenWrongIncarnation = "The document session token belongs to another incarnation.";
    private const string DocumentTokenOutOfScope = "The document session token belongs to another atomic partition or placement.";
    private const string DocumentTokenInvalidPosition = "The document session token position must be positive.";
    private const string DocumentTokenFuturePosition = "The document session token is beyond the current quorum-applied cut.";
    private const string DocumentTokenCorruptPosition = "The canonical applied position is invalid.";
    private const long DocumentTokenInitialPosition = 0;

    private static void ValidateDocumentSessionToken(IKeyValueView view, PartitionRef partition, CommitToken token)
    {
        var owner = ReadPlacementWitness(view, partition);
        if (token.Incarnation != owner.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenWrongIncarnation); }
        if (token.AtomicPartitionId != partition.AtomicPartitionId || token.OwnershipEpoch != owner.PlacementEpoch)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenOutOfScope); }
        if (token.Position <= DocumentTokenInitialPosition)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenInvalidPosition); }
        var bytes = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition);
        var applied = NativeSerialization.Deserialize<long>(bytes);
        if (applied < DocumentTokenInitialPosition)
        { throw Errors.Fail(ErrorCode.Corruption, DocumentTokenCorruptPosition); }
        if (token.Position > applied)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, DocumentTokenFuturePosition); }
    }
}
