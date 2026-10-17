namespace KeyLoad.Core.Features.InternalSerialization;

internal static class BlobQuotaFields
{
    internal const uint FormatVersion = 0;
    internal const uint Incarnation = 1;
    internal const uint ReservedBytes = 2;
    internal const uint ObjectKeys = 3;
    internal const uint Versions = 4;
    internal const uint Uploads = 5;
}
