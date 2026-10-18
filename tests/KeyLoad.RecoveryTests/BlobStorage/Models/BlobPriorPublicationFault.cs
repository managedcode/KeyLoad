using ManagedCode.Communication.Commands;
namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobPriorPublicationFault(Command<DatabaseCommand> Delete, Command<DatabaseCommand> Reclaim,
    Command<DatabaseCommand> QueueOperation, string[] OriginalImage, string[] DamagedImage, long Position,
    byte[] Key, byte[] OriginalRow);
