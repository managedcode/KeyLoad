namespace KeyLoad.Core.Features.BlobStorage;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.BlobRestoreMarker)]
internal sealed record BlobRestoreMarker(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.SourceIncarnation)] Guid SourceIncarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.TargetIncarnation)] Guid TargetIncarnation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.Phase)] BlobRestorePhase Phase,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.ExclusiveCursor)] ReadOnlyMemory<byte>? ExclusiveCursor,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.ReservedBytes)] long ReservedBytes,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.ObjectKeys)] int ObjectKeys,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.Versions)] int Versions,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.ActiveUploads)] int ActiveUploads,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.BlobRestoreMarkerFields.Resources)] int Resources);
