using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ValidateRemoteTransferOriginalSourceCommit(IKeyValueView view, RemoteTransferIntentRecord intent)
    {
        var original = intent.RemoteTarget?.OriginalSourceCommit
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        ValidateCommitToken(view, intent.Source.Partition, original,
            ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
    }
}
