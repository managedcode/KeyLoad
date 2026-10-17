using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClosedCommandRf3Provisioning
{
    internal static async Task<PostgresClosedCommandRf3State> CreateAsync(KeyLoadClient root,
        PostgresCompositionRf3Image image, CancellationToken token)
    {
        var admin = await PostgresRf3Seed.IdentityAsync(root, PostgresClosedCommandRf3Protocol.AdminMarker,
            Capability.DocumentsRead | Capability.Query, token);
        var administrator = admin.Principal with
        {
            ClusterAdministrator = true,
            PolicyEpoch = checked(admin.Principal.PolicyEpoch + PostgresClosedCommandRf3Protocol.EpochStep)
        };
        await SqlRf3Protocol.EqualAsync(administrator,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), administrator, token)));
        var target = await PostgresRf3Seed.IdentityAsync(root, PostgresClosedCommandRf3Protocol.TargetMarker,
            Capability.DocumentsRead | Capability.Query, token);
        var denied = target.Principal with
        {
            PolicyEpoch = checked(target.Principal.PolicyEpoch + PostgresClosedCommandRf3Protocol.EpochStep),
            Grants = target.Principal.Grants.Select(grant => grant with
            {
                Capabilities = grant.Capabilities & ~Capability.DocumentsRead
            }).ToImmutableArray()
        };
        var repaired = target.Principal with
        {
            PolicyEpoch = checked(denied.PolicyEpoch + PostgresClosedCommandRf3Protocol.EpochStep)
        };
        return new(admin with
        {
            Principal = administrator
        }, target, denied, repaired, Guid.NewGuid(), image);
    }
}
