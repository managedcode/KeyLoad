using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Search;

internal sealed record AnnProviderQualificationHistory(
    Command<DatabaseCommand> Operation, Result<object> Result, long Sequence, byte[] Payload,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[]? LocatorBytes);
