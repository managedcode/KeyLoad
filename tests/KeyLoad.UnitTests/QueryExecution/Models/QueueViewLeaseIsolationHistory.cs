using KeyLoad.Core;
using ManagedCode.Communication.Commands;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record QueueViewLeaseIsolationHistory(Command<DatabaseCommand> Operation, Result<object> Result,
    StoredOutcome Entry, byte[] OutcomeKey, byte[] Outcome, byte[] LocatorKey, byte[]? Locator);
