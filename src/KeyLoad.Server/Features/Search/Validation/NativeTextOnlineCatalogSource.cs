using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineCatalogSource
{
    internal static void RequireOriginal(NativeTextOnlineGenerationPin retained,
        OnlineTextCurrentPublication current, OnlineTextIndexMaintenanceResult result,
        int maximumRecords, ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> options)
    {
        retained.RequireActive();
        var generation = retained.Owner;
        var manifest = generation.Manifest;
        var baseCut = result.BaseCut;
        if (result.CommandId != current.CommandId || result.Consumer != current.Consumer
            || result.ConsumerGeneration != current.ConsumerGeneration || result.PublishedCut != current.PublishedCut
            || manifest.Scope.NodeId != baseCut.NodeId || manifest.Scope.Incarnation != baseCut.Incarnation
            || manifest.Scope.ReadGeneration != baseCut.ReadGeneration || manifest.Scope.Position != baseCut.Position
            || manifest.Scope.PolicyEpoch != baseCut.PolicyEpoch || manifest.Scope.SchemaVersion != baseCut.SchemaVersion
            || manifest.Scope.PrincipalId != current.PrincipalId || manifest.Scope.DataEpoch != current.Authority.DataEpoch
            || manifest.ResourceSha256 != result.PublishedCut.ResourceSha256
            || manifest.ThroughSequence != result.PublishedCut.ThroughSequence
            || manifest.AppliedPosition != result.PublishedCut.AppliedPosition)
        { throw NativeTextErrors.Mismatch(); }
        NativeTextIncrementalValidation.Manifest(manifest, manifest.Scope, current.Consumer,
            current.ConsumerGeneration, current.Authority.Placement, maximumRecords, budget, options);
        var path = Path.Combine(generation.Root, current.Authority.Leaf);
        NativeTextOnlineStagingFiles.RequireCommitted(path, current, result, budget, options);
        var envelope = Path.Combine(path, NativeTextIncrementalProtocol.ManifestFile);
        if (NativeTextOnlineManifestDigest.Calculate(envelope, budget, options) != current.Authority.ManifestSha256)
        { throw NativeTextErrors.Corrupt(); }
        using var actual = new NativeTextIncrementalNativeOwner(generation.Root, current.Authority.Leaf,
            current.PublishedCut.NodeId, options);
        NativeTextIncrementalPublishedInventory.Require(actual, manifest, budget, options, retained);
        budget.ChargeBytes(NativeSerialization.Measure(manifest.Files));
        if (Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(manifest.Files))) != result.IndexSha256
            || manifest.Records.Length != result.TrackedRecords)
        { throw NativeTextErrors.Corrupt(); }
        budget.Check();
    }

    internal static void RequireFresh(OnlineTextCurrentPublication current,
        OnlineTextIndexMaintenanceResult result, NativeTextIncrementalManifest manifest,
        NativeTextSeedCapture fresh, ReadExecutionBudget budget)
    {
        var original = result.PublishedCut;
        if (fresh.PrincipalId != current.PrincipalId || fresh.Incarnation != original.Incarnation
            || fresh.DataEpoch != current.Authority.DataEpoch || fresh.PolicyEpoch != original.PolicyEpoch
            || fresh.SchemaVersion != original.SchemaVersion || fresh.ResourceSha256 != original.ResourceSha256
            || fresh.Position < original.Position || fresh.AppliedPosition < original.AppliedPosition
            || fresh.ReadGeneration < original.ReadGeneration || fresh.UpperSequence != original.ThroughSequence)
        { throw NativeTextErrors.Mismatch(); }
        NativeTextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, budget);
        budget.Check();
    }
}
