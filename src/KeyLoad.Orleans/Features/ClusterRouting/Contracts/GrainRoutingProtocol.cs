namespace KeyLoad.Orleans;

internal static class GrainRoutingProtocol
{
    internal const string RequestAlias = "keyload.request.v2";
    internal const string CommandAlias = "keyload.partition.command.v1";
    internal const string ReadAlias = "keyload.database.read.v1";
    internal const string ExecuteAlias = "keyload.execute.v1";
    internal const string ExecuteStreamAlias = "keyload.execute-stream.v2";
    internal const string PayloadWireName = "payloadBase64Url";
    internal const string ReplyAlias = "keyload.request.reply.v1";
    internal const string ProgressAlias = "keyload.request.progress.v1";
    internal const string ContextAlias = "keyload.request.context.v1";
    internal const string CatalogPartition = "keyload:catalog";
    internal const string AuthorizationPartition = "keyload:authorization";
    internal const string InvalidRequest = "The internal request has an invalid scope, key, expiry or operation.";
    internal const string MissingPrincipal = "The internal request requires a persisted principal.";
    internal const string AdministrationRequired = "Cluster administration is required.";
    internal const string ReplyBudgetExceeded = "The database reply exceeds its byte budget.";
    internal const int RequestInterfaceVersion = 2;
    internal const int CapabilityInterfaceVersion = 1;
    internal const int EnvelopeMetadataBytes = 65_536;
    internal const int MaximumReplyBytes = 16_777_216;
    internal static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan MaximumFuture = TimeSpan.FromMinutes(2);
}
