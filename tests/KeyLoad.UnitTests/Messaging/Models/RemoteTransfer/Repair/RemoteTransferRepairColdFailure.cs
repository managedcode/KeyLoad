using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferRepairColdFailure(CommandRequest Command, Result<object> Result, byte[] Bytes);
