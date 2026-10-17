namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Freezes the private native inventory envelope without defining its Core payload.</summary>
internal static class ResourceOwnerInventoryNativeProtocol
{
    internal const int Version = 1;
    internal const string ServiceMethodAlias = "read-resource-inventory";
    internal const string RequestAlias = "keyload.resource-execution.resource-owner-inventory-native-request.v1";
    internal const string ReplyAlias = "keyload.resource-execution.resource-owner-inventory-native-reply.v1";
    internal const string RequestPurpose = "keyload-resource-owner-inventory-native-request-v1";
    internal const string ReplyPurpose = "keyload-resource-owner-inventory-native-reply-v1";
    internal const string Unavailable = "The original bounded resource owner inventory is unavailable.";
    internal const string InvalidProof = "The resource owner inventory envelope is invalid.";
}
