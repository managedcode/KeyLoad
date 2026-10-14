namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsProbeNameValidation
{
    private const string TemporaryPrefix = "tmp-";
    private const string TemporarySuffix = ".tmp";
    private const string JsonSuffix = ".json";

    internal static bool IsAllowedFileName(string name)
    {
        if (IsNativeOperationName(name))
        {
            return true;
        }
        if (name == RequestCqrsProbeFixtureProtocol.OwnerFileName)
        {
            return true;
        }
        if (IsTemporaryName(name))
        {
            return true;
        }
        if (HasGuidFile(name, RequestCqrsProbeFixtureProtocol.ArmFilePrefix, 1))
        {
            return true;
        }
        if (HasGuidFile(name, RequestCqrsProbeFixtureProtocol.ReleaseFilePrefix, 2))
        {
            return true;
        }
        return name.StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix, StringComparison.Ordinal)
            && name.EndsWith(JsonSuffix, StringComparison.Ordinal)
            && RequestCqrsProbeFileNames.IsMarkerName(name);
    }

    internal static bool IsTemporaryName(string name)
    {
        if (!name.StartsWith(TemporaryPrefix, StringComparison.Ordinal)
            || !name.EndsWith(TemporarySuffix, StringComparison.Ordinal))
        {
            return false;
        }
        var id = name.AsSpan(TemporaryPrefix.Length, name.Length - TemporaryPrefix.Length - TemporarySuffix.Length);
        return Guid.TryParseExact(id, "N", out var temporaryId)
            && temporaryId.ToString("N").AsSpan().SequenceEqual(id);
    }

    private static bool HasGuidFile(string name, string prefix, int guidCount)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(JsonSuffix, StringComparison.Ordinal))
        {
            return false;
        }
        var parts = name[prefix.Length..^JsonSuffix.Length].Split('-');
        if (parts.Length != guidCount)
        {
            return false;
        }
        foreach (var part in parts)
        {
            if (!Guid.TryParseExact(part, "N", out var value) || value.ToString("N") != part)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNativeOperationName(string name)
    {
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsMovementJournalInventory.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsWaitForIndexReadCutInventory.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsQueryReadCutInventory.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsNativeReplayInventory.IsPlanName(name)
            || KeyLoad.Server.Features.ClusterRouting.RequestCqrsNativeReplayInventory.IsWitnessName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsNativeModelInventory.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsEventVectorExpiryJson.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsReplicaSnapshotJson.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.ConnectionProbeInventory.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeMigrationValidation.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeActivationValidation.IsName(name))
        {
            return true;
        }
        if (KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeLiveValidation.IsName(name))
        {
            return true;
        }
        return false;
    }
}
