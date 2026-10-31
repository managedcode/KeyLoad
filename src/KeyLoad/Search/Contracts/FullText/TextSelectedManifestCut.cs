namespace KeyLoad.Server.Search;

/// <summary>A validated generation manifest and its record maps, shared read-only by every reader of that manifest.</summary>
internal sealed record TextSelectedManifestCut(DateTime Written, long Length, TextIncrementalManifest Manifest,
    Dictionary<EntityRef, TextIncrementalRecord> ByReference, Dictionary<ulong, TextIncrementalRecord> ById)
{
    internal static TextSelectedManifestCut Create(TextIncrementalManifest manifest, DateTime written = default, long length = 0)
        => new(written, length, manifest, manifest.Records.ToDictionary(record => record.Reference),
            manifest.Records.ToDictionary(record => record.Id));
}
