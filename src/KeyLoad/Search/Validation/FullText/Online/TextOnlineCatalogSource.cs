using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextOnlineCatalogSource
{
    internal static void RequireOriginal(TextOnlineGenerationPin retained,
        OnlineTextCurrentPublication current, OnlineTextIndexMaintenanceResult result,
        ReadExecutionBudget budget, IOptions<TextExecutionOptions> options)
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
        {
            throw TextErrors.Mismatch();
        }
        TextIncrementalValidation.Manifest(manifest, manifest.Scope, current.Consumer,
            current.ConsumerGeneration, current.Authority.Placement, budget, options);
        var path = Path.Combine(generation.Root, current.Authority.Leaf);
        TextOnlineStagingFiles.RequireCommitted(path, current, result, budget, options);
        var envelope = Path.Combine(path, TextIncrementalProtocol.ManifestFile);
        if (TextOnlineManifestDigest.Calculate(envelope, budget, options) != current.Authority.ManifestSha256)
        {
            throw TextErrors.Corrupt();
        }
        using var actual = new TextIncrementalOwner(generation.Root, current.Authority.Leaf,
            current.PublishedCut.NodeId, options);
        TextIncrementalPublishedInventory.Require(actual, manifest, budget, options, retained);
        if (Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(manifest.Files))) != result.IndexSha256
            || manifest.Records.Length != result.TrackedRecords)
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
    }

    internal static void RequireFresh(OnlineTextCurrentPublication current,
        OnlineTextIndexMaintenanceResult result, TextIncrementalManifest manifest,
        TextSeedCapture fresh, ReadExecutionBudget budget)
    {
        var original = result.PublishedCut;
        if (fresh.PrincipalId != current.PrincipalId || fresh.Incarnation != original.Incarnation
            || fresh.DataEpoch != current.Authority.DataEpoch || fresh.PolicyEpoch != original.PolicyEpoch
            || fresh.SchemaVersion != original.SchemaVersion || fresh.ResourceSha256 != original.ResourceSha256
            || fresh.Position < original.Position || fresh.AppliedPosition < original.AppliedPosition
            || fresh.ReadGeneration < original.ReadGeneration || fresh.UpperSequence != original.ThroughSequence)
        {
            throw TextErrors.Mismatch();
        }
        TextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, budget);
        budget.Check();
    }
}
