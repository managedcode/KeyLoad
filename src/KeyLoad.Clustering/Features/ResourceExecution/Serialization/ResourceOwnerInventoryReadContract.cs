namespace KeyLoad.Clustering;

internal static class ResourceOwnerInventoryReadContract
{
    internal const int Version = 1;
    internal const string OperationAlias = "keyload.resource-owner-inventory.native-read-operation.v1";
    internal const string RequestAlias = "keyload.resource-owner-inventory.native-read-request.v1";
    internal const string ReplyAlias = "keyload.resource-owner-inventory.native-read-reply.v1";
    internal const int VersionField = 0;
    internal const int OperationField = 1;
    internal const int RequestField = 2;
    internal const int ControlField = 3;
    internal const int ObservedOwnerField = 4;
    internal const int ContinuationField = 5;
    internal const int OwnerCutField = 6;
    internal const int DestinationVoterField = 7;
    internal const int DestinationSiloAddressField = 8;
    internal const int OriginalPolicyEpochField = 9;
    internal const int MaximumReadBytesField = 10;
    internal const int MaximumExaminedRecordsField = 11;
    internal const int MaximumResultBytesField = 12;
    internal const int ReplyControlField = 0;
    internal const int ReplyPageField = 1;
    internal const int ValidatedField = 2;
    internal const int ReadBytesField = 3;
    internal const int ExaminedRecordsField = 4;
}
