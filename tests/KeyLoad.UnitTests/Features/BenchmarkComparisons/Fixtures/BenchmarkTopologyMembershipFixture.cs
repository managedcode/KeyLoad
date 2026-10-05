using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class BenchmarkTopologyMembershipFixture : IDisposable
{
    internal const string MembershipKeyName = "replica-benchmark-membership";
    internal const string PrivateCanary = "private-membership-canary";
    private const string DirectoryPrefix = "keyload-benchmark-membership-";
    private const string GuidFormat = "N";
    private const string VoterPrefix = "voter-";
    private const string LocalVoter = VoterPrefix + "1";
    internal static readonly byte[] MembershipKey = KeyCodec.Encode(MembershipKeyName);
    internal static readonly byte[] HardStateKey = KeyCodec.Encode(ReplicaProtocol.StateKey);

    internal BenchmarkTopologyMembershipFixture()
    {
        DirectoryPath = Path.Combine(Resolve(new(Path.GetTempPath())), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        Incarnation = Guid.NewGuid();
    }

    internal string DirectoryPath { get; }
    internal Guid Incarnation { get; }

    internal ReplicaConfiguration Configuration(int nodes, bool benchmark = true)
        => new(LocalVoter, Enumerable.Range(1, nodes)
            .Select(number => VoterPrefix + number.ToString(CultureInfo.InvariantCulture)).ToImmutableArray(),
            DirectoryPath, Incarnation)
        { BenchmarkTopology = benchmark };

    internal ZoneTreeStore Open(Action<CommitStage, long, int>? observer = null)
        => new(new(DirectoryPath) { Incarnation = Incarnation, FaultObserver = observer });

    internal static byte[]? Membership(ZoneTreeStore store)
        => store.Read(view => view.ReadOwnedValue(MembershipKey));

    internal static byte[]? HardState(ZoneTreeStore store)
        => store.Read(view => view.ReadOwnedValue(HardStateKey));

    internal static KeyLoadException Reject(ZoneTreeStore store, ReplicaConfiguration configuration)
        => Assert.ThrowsExactly<KeyLoadException>(() => { using var log = new DurableReplicaLog(store, UnitExecutionOptions.ReplicaConfiguration(configuration)); });

    private static string Resolve(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory.FullName;
        }
        var entry = new DirectoryInfo(Path.Combine(Resolve(directory.Parent), directory.Name));
        return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
