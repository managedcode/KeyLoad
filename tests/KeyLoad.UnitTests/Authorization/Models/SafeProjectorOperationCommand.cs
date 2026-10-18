using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Authorization;

// The fixture retains its caller input separately from the actual persisted command outcome.
internal sealed record SafeProjectorOperationCommand(Command<DatabaseCommand> Operation, StoredOutcome Outcome,
    Result<object> Result, CommitToken ExpectedToken, long CreatorEpoch, SafeProjectorOperationOutcomeImage OriginalOutcome)
{
    private const string Redacted = "SafeProjectorOperationCommand(<private>)";
    public override string ToString() => Redacted;
}
