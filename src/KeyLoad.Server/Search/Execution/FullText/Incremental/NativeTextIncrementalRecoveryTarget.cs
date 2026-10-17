using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

/// <summary>Reconstructs only the original complete intent, never a replacement canonical seed.</summary>
internal static class NativeTextIncrementalRecoveryTarget
{
    internal static NativeTextIncrementalManifest FromIntent(NativeTextIncrementalIntent original,
        NativeTextIncrementalManifest? published, ReadExecutionBudget budget)
    {
        budget.Check();
        var through = original.Bootstrap ? original.SourceUpperSequence : original.ThroughSequence;
        var target = new NativeTextIncrementalManifest(NativeTextIncrementalProtocol.ManifestFormatVersion,
            original.Scope, original.Consumer, original.Generation, original.Placement, through,
            original.AppliedPosition, original.NextRecord, original.Records, published?.Files ?? [],
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion,
            original.ResourceSha256, original.Bootstrap, published?.LastSettledCheckpointRequest);
        budget.Check();
        return target;
    }
}
