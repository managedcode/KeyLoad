namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeCold
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        AtomicMembershipScopeState state, CancellationToken token)
    {
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeActivationRf3ColdOutcome.VerifyAsync(wave, state.Identity,
                state.Second, state.SecondReceipt, token);
            await NativeActivationMigrationRf3Ownership.RequirePhysicalAsync(state.Owners,
                await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, token));
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                wave.Profile.AdminKey, async admin =>
                {
                    return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                        TwoRf3MembershipProtocol.Node2, state.Identity.Secret, async caller =>
                        {
                            await AtomicMembershipScopeReplay.RunAsync(admin, caller, state, token);
                            return true;
                        }, token);
                }, token);
        }
    }
}
