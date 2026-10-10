namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferRepairColdFailure(CommandRequest Command, OperationResult Result, byte[] Bytes);
