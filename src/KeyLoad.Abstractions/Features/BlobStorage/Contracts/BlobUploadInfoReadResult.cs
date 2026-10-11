namespace KeyLoad;

/// <summary>Contains creator-authorized upload progress, or successful absence.</summary>
/// <param name="Upload">The genuine upload state; null means no matching upload exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.BlobUpload)]
public sealed record BlobUploadInfoReadResult([property: Orleans.Id(0)] BlobUploadInfo? Upload);
