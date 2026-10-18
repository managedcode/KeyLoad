using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record QueueViewLeaseIsolationHistory(Command<DatabaseCommand> Operation, Result<object> Result,
    StoredOutcome Entry, byte[] OutcomeKey, byte[] Outcome, byte[] LocatorKey, byte[]? Locator);
