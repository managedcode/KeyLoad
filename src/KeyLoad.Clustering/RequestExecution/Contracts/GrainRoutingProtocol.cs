namespace KeyLoad.Clustering;

internal static class GrainRoutingProtocol
{
    internal const string RequestAlias = "keyload.connection";
    internal const string ExecutionAlias = "keyload.request.execution";
    internal const string RequestGrainAlias = "keyload.request";
    internal const string ExecuteAlias = "keyload.execute";
    internal const string ExecuteStreamAlias = "keyload.execute-stream";
    internal const string ReplyAlias = "keyload.request.reply";
    internal const string ProgressAlias = "keyload.request.progress";
    internal const string ContextAlias = "keyload.request.context";
    internal const string CloseConnectionAlias = "keyload.connection.close";
    internal const string ReleaseRequestAlias = "keyload.request.release";
    internal const string CatalogPartition = CatalogRoute.PartitionId;
    internal const string AuthorizationPartition = "keyload:authorization";
    internal const string InvalidRequest = "The internal request has an invalid scope, key, expiry or operation.";
    internal const string MissingPrincipal = "The internal request requires a persisted principal.";
    internal const string AdministrationRequired = "Cluster administration is required.";
    internal const int RequestInterfaceVersion = 7;
    internal const int ExecutionInterfaceVersion = 1;
    internal const int RequestGrainInterfaceVersion = 1;
}
