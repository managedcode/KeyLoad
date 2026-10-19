using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

/// <summary>Records only closed phase metadata before the original fault boundary is armed.</summary>
internal sealed class SharedDomainChildProgressRecorder(string directory, ZoneTreeStore store)
{
    private readonly long started = TimeProvider.System.GetTimestamp();

    internal void Record(SharedDomainChildProgressPhase phase,
        int seedOrdinal = SharedDomainChildProgressProtocol.InitialOrdinal)
    {
        var witness = new SharedDomainChildProgress(SharedDomainChildProgressProtocol.Version, phase,
            seedOrdinal, TimeProvider.System.GetElapsedTime(started).TotalMilliseconds, store.Position);
        var bytes = OrleansSerialization.Serialize(witness);
        if (bytes.Length > SharedDomainChildProgressProtocol.MaximumBytes)
        {
            throw new InvalidDataException(SharedDomainChildProgressProtocol.Invalid);
        }
        File.WriteAllBytes(Path.Combine(directory, SharedDomainChildProgressProtocol.FileName), bytes);
    }
}
