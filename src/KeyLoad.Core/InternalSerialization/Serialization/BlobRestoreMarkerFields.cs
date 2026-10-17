namespace KeyLoad.Core.Features.InternalSerialization;

internal static class BlobRestoreMarkerFields
{
    internal const uint FormatVersion = 0;
    internal const uint SourceIncarnation = 1;
    internal const uint TargetIncarnation = 2;
    internal const uint Phase = 3;
    internal const uint ExclusiveCursor = 4;
    internal const uint ReservedBytes = 5;
    internal const uint ObjectKeys = 6;
    internal const uint Versions = 7;
    internal const uint ActiveUploads = 8;
    internal const uint Resources = 9;
}
