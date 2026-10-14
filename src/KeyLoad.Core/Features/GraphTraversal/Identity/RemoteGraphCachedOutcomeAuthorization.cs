using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ReauthorizeRemoteGraphOutcome(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome previous)
    {
        if (previous.Result.Error is not null)
        {
            return;
        }
        switch (operation.Kind)
        {
            case OperationKind.RemoteGraphSource:
            {
                var retained = previous.Result.Get<RemoteGraphSourceResultV1>();
                if (retained?.Original is null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
                }
                _ = RequireRemoteGraphSource(view, principal, retained.Original);
                break;
            }
            case OperationKind.RemoteGraphReceive:
            {
                var retained = previous.Result.Get<RemoteGraphReceiverResultV1>();
                if (retained?.Original?.SourceProof is null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
                }
                _ = RequireRemoteGraphReceiver(view, principal, retained.Original.SourceProof);
                break;
            }
            case OperationKind.RemoteGraphComplete:
            {
                var retained = previous.Result.Get<RemoteGraphCompletionResultV1>();
                if (retained?.Original?.Receipt is null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
                }
                _ = RequireRemoteGraphCompletion(view, principal, retained.Original.Receipt);
                break;
            }
        }
    }
}
