using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.Authorization;

// Private fixture observation, never a persisted or public transport contract.
internal sealed record SafeProjectorOperationCommand(ReplicaEntry Entry, OperationResult Result, CommitToken ExpectedToken, long CreatorEpoch, SafeProjectorOperationOutcomeImage OriginalOutcome)
{
    private const string MissingOriginal = "The original safe projector command is absent from its retained entry.";
    private const string Redacted = "SafeProjectorOperationCommand(<private>)";
    internal ReplicatedOperation Operation => Entry.Operation ?? throw new InvalidOperationException(MissingOriginal);
    public override string ToString() => Redacted;
}
