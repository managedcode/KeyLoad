using P = KeyLoad.FunctionalTests.Authorization.NativePolicyEpochProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class NativePolicyEpochWarm
{
    internal static async Task RunAsync(NativePolicyEpochState state, CancellationToken token)
    {
        await NativePolicyEpochProvision.SeedAsync(state, token);
        await NativePolicyEpochProvision.ResourceAsync(state, state.Resource with
        {
            SchemaVersion = 2L,
            FieldPolicies = [new(P.Path, P.Classification, P.Grant, P.UseGrant)]
        }, 1L, token);
        await NativePolicyEpochAssertions.ReadAsync(state, false, false, token);
        await NativePolicyEpochProvision.PrincipalAsync(state, state.Reader with
        {
            PolicyEpoch = 2L,
            FieldGrants = [P.Grant]
        }, token);
        await NativePolicyEpochAssertions.ReadAsync(state, true, false, token);
        await NativePolicyEpochProvision.PrincipalAsync(state, state.Reader with
        {
            PolicyEpoch = 3L,
            Grants = []
        }, token);
        await NativePolicyEpochRefusals.DeniedAsync(state, token);
        await NativePolicyEpochProvision.PrincipalAsync(state, state.Reader with
        {
            PolicyEpoch = 4L,
            Grants = [new(state.Partition.DatabaseId, P.Collection, Capability.DocumentsRead)]
        }, token);
        await NativePolicyEpochRefusals.CancelAsync(state);
        await NativePolicyEpochAssertions.ReadAsync(state, true, false, token);
        await NativePolicyEpochProvision.HealthyAsync(state, token);
        await NativePolicyEpochRefusals.ReplayAsync(state, token);
    }

}
