using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ResourcePairBindingState(TestDatabase source, ResourcePairBindingFault fault)
{
    internal TestDatabase Source { get; } = source;
    internal ResourcePairBindingFault Fault { get; } = fault;
    internal List<(ReplicatedOperation Original, OperationResult Result, byte[] Outcome)> Originals { get; } = [];
    internal List<(string Id, long Sequence, DateTimeOffset Time)> Mixed { get; } = [];
    internal List<(string Id, string Json)> Rows { get; } = [];
    internal List<BlobMetadata> BlobHeads { get; } = [];
    internal string ReplicaDirectory => Source.Directory + TestDatabaseReplicaDirectory.Suffix;
    internal StoreIdentity ReplicaIdentity { get; } = ReadReplica(source);
    internal (long LastIndex, long CommittedIndex, long Term) ExpectedJournal
    {
        get; set;
    }
    internal Dictionary<string, (byte[] Definition, byte[] Lifetime)> Pairs { get; } = new(StringComparer.Ordinal);
    internal static StoreIdentity ReadReplica(TestDatabase source)
        => ZoneTreeIdentityFile.Read(Path.Combine(source.Directory + TestDatabaseReplicaDirectory.Suffix,
            ZoneTreePersistenceFormat.IdentityFileName), source.StorageExecution.Value.MaximumIdentityFileBytes,
            source.StorageExecution.Value.StreamBufferBytes);
}
