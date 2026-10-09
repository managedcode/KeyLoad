namespace KeyLoad.Orleans;

internal static class ControlledBlobReadProtocol
{
    internal const string Purpose = "keyload-controlled-blob-read-v1";
    internal const string RequestAlias = "keyload.orleans.controlled-blob-read-request.v1";
    internal const string ResultAlias = "keyload.orleans.controlled-blob-read-result.v1";
    internal const long EmptyBytes = 0;
    internal const int EmptyRecords = 0;
}
