namespace KeyLoad.Core.Features.Messaging;

internal sealed record RemoteTransferSourceDispatch(RemoteTransferIntentClaims Claims,
    RemoteTransferIntentRecord Intent, RemoteTransferRemoteTarget Target,
    long PolicyEpoch, string FieldHeaderDigest, CommitToken SourceCut);
