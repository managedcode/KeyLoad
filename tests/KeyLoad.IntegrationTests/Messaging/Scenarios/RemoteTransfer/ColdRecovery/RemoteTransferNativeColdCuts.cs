using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeColdCuts
{
    private const string DatabaseDirectory = "database";
    private const string MissingCut = "The complete stopped native transfer cut is absent.";
    private const string InvalidOwners = "The retained native discovery does not identify all six original owners.";

    internal static async Task<IReadOnlyDictionary<string, NativeCanonicalModelCutWitness>> CaptureAndRestartAsync(
        TwoRf3MembershipWave wave, NativeCanonicalModelCutRequest request,
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> retainedSignedOwners, DatabaseLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wave);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        NativeCanonicalModelCutCodec.ValidateRequest(request);
        var owners = RequireOwners(retainedSignedOwners);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var storeNodes = await RemoteTransferNativeColdIdentities.ReadAsync(wave, owners, cancellationToken)
                .ConfigureAwait(false);
            var root = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
            var cuts = ReadAll(wave, root, request, owners, storeNodes, limits, cancellationToken);
            await wave.RestartJoinedAsync(cancellationToken).ConfigureAwait(false);
            return cuts;
        }
        catch (Exception)
        {
            wave.RetainRoots();
            throw;
        }
    }

    private static Dictionary<string, ReplicaSiloDiscovery> RequireOwners(
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Count != TwoRf3MembershipProtocol.NodeCount)
        {
            throw new InvalidOperationException(InvalidOwners);
        }
        var voters = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var owned = new Dictionary<string, ReplicaSiloDiscovery>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            if (!original.TryGetValue(node, out var owner) || owner is null || !owner.TransportReady
                || owner.Incarnation == Guid.Empty || string.IsNullOrWhiteSpace(owner.ClusterId)
                || string.IsNullOrWhiteSpace(owner.VoterId) || !voters.Add(owner.VoterId)
                || string.IsNullOrWhiteSpace(owner.SiloAddress)
                || owner.SiloAddress.Length > ReplicaTransportProtocol.MaximumAddressCharacters
                || !addresses.Add(owner.SiloAddress)
                || global::Orleans.Runtime.SiloAddress.FromParsableString(owner.SiloAddress).ToParsableString()
                    != owner.SiloAddress)
            {
                throw new InvalidOperationException(InvalidOwners);
            }
            owned.Add(node, owner);
        }
        if (owned.Values.Select(owner => owner.ClusterId).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new InvalidOperationException(InvalidOwners);
        }
        return owned;
    }

    private static Dictionary<string, NativeCanonicalModelCutWitness> ReadAll(
        TwoRf3MembershipWave wave, string root, NativeCanonicalModelCutRequest request,
        Dictionary<string, ReplicaSiloDiscovery> owners, IReadOnlyDictionary<string, Guid> storeNodes,
        DatabaseLimits limits, CancellationToken token)
    {
        var failures = new List<Exception>();
        var cuts = new Dictionary<string, NativeCanonicalModelCutWitness>(StringComparer.Ordinal);
        ServerFailureObserver.Observe(() =>
        {
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                token.ThrowIfCancellationRequested();
                cuts.Add(node, ReadStopped(wave, root, node, request, owners[node], storeNodes[node], limits, token));
            }
        }, failures);
        ServerFailureObserver.Observe(wave.AssertAllNodeLocksReleased, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return cuts;
    }

    private static NativeCanonicalModelCutWitness ReadStopped(TwoRf3MembershipWave wave, string root,
        string node, NativeCanonicalModelCutRequest request, ReplicaSiloDiscovery retainedSignedOwner,
        Guid originalNodeId, DatabaseLimits limits, CancellationToken token)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        NativeCanonicalModelCutWitness? cut = null;
        ServerFailureObserver.Observe(() =>
        {
            token.ThrowIfCancellationRequested();
            var directory = Path.Combine(root, node, DatabaseDirectory);
            if (!Directory.Exists(directory))
            {
                throw new InvalidOperationException(MissingCut);
            }
            var configured = wave.movementMaxFrameBytes is { } maximum
                ? new ZoneTreeStorageExecutionOptions { MaxFrameBytes = maximum } : null;
            store = ZoneTreeExistingStore.Open(new ZoneTreeStoreOptions(directory)
            {
                Incarnation = retainedSignedOwner.Incarnation
            }, originalNodeId,
                IntegrationExecutionOptions.StorageExecution(configured));
            cut = store.Read(view => new NativeCanonicalModelCutReader(IntegrationExecutionOptions.DatabaseLimits(limits), token)
                .Read(view, request, retainedSignedOwner, store.Position));
            NativeCanonicalModelCutCodec.RequireEnvelope(cut, token);
        }, failures);
        if (store is { } owned)
        {
            ServerFailureObserver.Observe(owned.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return cut ?? throw new InvalidOperationException(MissingCut);
    }
}
