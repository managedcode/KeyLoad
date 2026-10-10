namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferExternalReceiptFields
{
    internal const uint Purpose = 0;
    internal const uint SourceOwner = 1;
    internal const uint DestinationOwner = 2;
    internal const uint Source = 3;
    internal const uint Destination = 4;
    internal const uint TransferId = 5;
    internal const uint LogicalPrincipalId = 6;
    internal const uint Fingerprint = 7;
    internal const uint OriginalTargetReceiptToken = 8;
    internal const uint TargetCommit = 9;
    internal const uint SourceCut = 10;
    internal const uint OriginalTargetReceiptDigest = 11;
}
