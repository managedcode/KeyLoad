using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3AuthorizationFlow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, NativeRemoteGraphRf3Seed seed, CancellationToken token)
    {
        var failures = new List<Exception>();
        var denied = new Dictionary<string, PrincipalRecord>(StringComparer.Ordinal);
        var policyChanged = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            policyChanged = true;
            await RevokeAsync(wave, seed, denied, token);
            var baseline = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, seed.Directory, token);
            await wave.RestartJoinedAsync(token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
                async caller => { await NativeRemoteGraphAuthorizationCallers.RequireUpsertDeniedAsync(caller, seed.Upsert, token); return true; }, token);
            var refused = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, seed.Directory, token);
            await NativeBlobWholeColdAssertions.RequireUnchangedAsync(baseline, refused, token);
            await wave.RestartJoinedAsync(token);
            await RestoreAsync(wave, seed, denied, token);
            policyChanged = false;
        }, failures);
        if (policyChanged)
        {
            using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (wave.application is null)
                {
                    await wave.RestartJoinedAsync(cleanup.Token);
                }
                await RestoreAsync(wave, seed, denied, cleanup.Token);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RevokeAsync(TwoRf3MembershipWave wave, NativeRemoteGraphRf3Seed seed,
        Dictionary<string, PrincipalRecord> denied, CancellationToken token)
    {
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            denied.Add(node, await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, wave.Profile.AdminKey,
                root => NativeRemoteGraphAuthorizationPolicy.RevokeWriteAsync(root.Sdk, seed.Principal, token), token));
        }
    }

    private static async Task RestoreAsync(TwoRf3MembershipWave wave, NativeRemoteGraphRf3Seed seed,
        IReadOnlyDictionary<string, PrincipalRecord> denied, CancellationToken token)
    {
        foreach (var (node, actualDenied) in denied)
        {
            var restored = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, wave.Profile.AdminKey,
                root => NativeRemoteGraphAuthorizationPolicy.RestoreWriteAsync(root.Sdk, seed.Principal, actualDenied, token), token);
            seed.RestoredPrincipals[node] = restored;
        }
    }
}
