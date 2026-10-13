namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeKnownNames
{
    private const string TemporaryFileSuffix = ".tmp";
    internal static bool KnownName(string name) => RequestCqrsWaitForIndexReadCutInventory.IsName(name)
        || RequestCqrsMovementJournalInventory.IsName(name)
        || RequestCqrsQueryReadCutInventory.IsName(name)
        || RequestCqrsNativeReplayInventory.IsPlanName(name)
        || RequestCqrsNativeReplayInventory.IsWitnessName(name) || RequestCqrsNativeModelInventory.IsName(name)
        || RequestCqrsEventVectorExpiryJson.IsName(name)
        || RequestCqrsReplicaSnapshotJson.IsName(name)
        || ConnectionProbeInventory.IsName(name)
        || RequestCqrsProbeMigrationValidation.IsName(name)
        || RequestCqrsProbeLiveValidation.IsName(name)
        || RequestCqrsProbeActivationValidation.IsName(name)
        || name == RequestCqrsProbeProtocol.OwnerFile
        || IsGuidName(name, RequestCqrsProbeProtocol.ArmFilePrefix, RequestCqrsProbeProtocol.JsonFileSuffix) || IsGuidName(name, RequestCqrsProbeProtocol.TemporaryFilePrefix, TemporaryFileSuffix)
        || name.StartsWith(RequestCqrsProbeProtocol.ReleaseFilePrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal)
        || name.StartsWith(RequestCqrsProbeProtocol.MarkerFilePrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal);

    private static bool IsGuidName(string name, string prefix, string suffix)
    {
        const string CompactIdentityFormat = "N";
        const int CompactGuidCharacterCount = 32;

        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length), CompactIdentityFormat, out var value))
        { return false; }
        return name.AsSpan(prefix.Length, CompactGuidCharacterCount).SequenceEqual(value.ToString(RequestCqrsProbeProtocol.SessionIdFormat));
    }

}
