using ManagedCode.Communication;
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record RemoteTransferRepairColdFailure(CommandRequest Command, Result<object> Result, byte[] Bytes);
