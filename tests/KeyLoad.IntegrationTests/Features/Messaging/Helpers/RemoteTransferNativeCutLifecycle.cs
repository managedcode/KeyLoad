using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeCutLifecycle
{
    internal static async Task<IReadOnlyDictionary<string, NativeCanonicalModelCutWitness>> LiveAsync(
        TwoRf3MembershipWave wave, NativeCanonicalModelCutRequest scope, CancellationToken token)
    {
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        return await NativeCanonicalModelCutClient.ReadSixAsync(wave, wave.Profile.AdminKey, scope,
            membership.Owners, membership.Secrets, token);
    }

    internal static async Task ColdAsync(TwoRf3MembershipWave wave, RemoteTransferNativeAdministrator administrator,
        NativeCanonicalModelCutRequest scope, IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> before,
        CancellationToken token)
    {
        using var original = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        await administrator.DisposeAsync();
        var stopped = await RemoteTransferNativeColdCuts.CaptureAndRestartAsync(wave, scope,
            original.Owners, new DatabaseLimits(), token);
        RequireUnchanged(before, stopped, token);
        await administrator.ReopenAsync(wave, token);
        using var current = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await Assert.That(current.Owners[node].SiloAddress).IsNotEqualTo(original.Owners[node].SiloAddress);
            await Assert.That(current.Owners[node].Incarnation).IsEqualTo(original.Owners[node].Incarnation);
            await Assert.That(current.Owners[node].VoterId).IsEqualTo(original.Owners[node].VoterId);
        }
        var live = await NativeCanonicalModelCutClient.ReadSixAsync(wave, wave.Profile.AdminKey, scope,
            current.Owners, current.Secrets, token);
        RequireUnchanged(stopped, live, token);
    }

    internal static void RequireUnchanged(IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> before,
        IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> after, CancellationToken token)
    {
        if (before.Count != TwoRf3MembershipProtocol.NodeCount || after.Count != before.Count)
        {
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            NativeCanonicalModelCutAssertions.RequireUnchanged(before[node], after[node], token);
        }
    }

    internal static async Task RequireTargetAsync(IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> cuts,
        RemoteTransferColdSeed seed, CommandRequest accept, RemoteModelNativeRequest original,
        RemoteTransferPostAwaitNativeCut committed)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes[3..])
        {
            await committed.RequireAsync(RemoteTransferNativeCommittedCut.Read(cuts[node], seed, accept, original));
        }
    }
}
