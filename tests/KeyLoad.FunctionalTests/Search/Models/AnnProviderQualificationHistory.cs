using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.Search;

internal sealed record AnnProviderQualificationHistory(
    Command<DatabaseCommand> Operation, Result<object> Result, long Sequence, byte[] Payload,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[]? LocatorBytes);
