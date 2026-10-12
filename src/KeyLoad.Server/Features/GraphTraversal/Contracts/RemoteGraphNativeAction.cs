namespace KeyLoad.Server.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphNativeProtocol.ActionAlias)]
internal enum RemoteGraphNativeAction
{
    TargetPreparation = 0,
    SourceIntent = 1,
    ReceiverApply = 2,
    ReceiverReceipt = 3,
    IncomingSourceProof = 4,
    ReceiverCandidates = 5,
    IncomingFinalize = 6
}
