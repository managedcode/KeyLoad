using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

/// <summary>Reconstructs only the original complete intent, never a replacement canonical seed.</summary>
internal static class TextIncrementalRecoveryTarget
{
    internal static TextIncrementalManifest FromIntent(TextIncrementalIntent original,
        TextIncrementalManifest? published, ReadExecutionBudget budget)
    {
        budget.Check();
        var through = original.Bootstrap ? original.SourceUpperSequence : original.ThroughSequence;
        var target = new TextIncrementalManifest(TextIncrementalProtocol.ManifestFormatVersion,
            original.Scope, original.Consumer, original.Generation, original.Placement, through,
            original.AppliedPosition, original.NextRecord, original.Records, published?.Files ?? [],
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion,
            original.ResourceSha256, original.Bootstrap, published?.LastSettledCheckpointRequest);
        budget.Check();
        return target;
    }
}
