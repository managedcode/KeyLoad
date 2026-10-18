using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class AdversarialAuthorityTrial
{
    internal static async Task<AdversarialAuthorityState> WarmAsync(ClusterFixture fixture, CancellationToken token)
    {
        var owned = await SeedAsync(fixture, token);
        var foreign = await SeedAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, owned.Scenario.Partition, Capability.DocumentsRead, token);
        var state = new AdversarialAuthorityState(owned, foreign, identity);
        await WithClientsAsync(fixture, state, true, token);
        return state;
    }

    internal static async Task WithClientsAsync(ClusterFixture fixture, AdversarialAuthorityState state,
        bool changePolicy, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, token);
            try
            {
                using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
                try
                {
                    await RunClientsAsync(fixture, http, session, state, changePolicy, token);
                }
                catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
                catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunClientsAsync(ClusterFixture fixture, HttpClient http, McpOfficialClient session,
        AdversarialAuthorityState state, bool changePolicy, CancellationToken token)
    {
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var reader = new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
        if (changePolicy)
        {
            await AdversarialAuthorityContinuation.RunAsync(fixture, root, reader, session, state, token);
        }
        else
        {
            await AdversarialAuthorityAssertions.HealthyAsync(reader, session, state, token);
            await AdversarialAuthorityRefusals.ForeignAsync(root, reader, session, state, token);
        }
        await ReplayAsync(fixture, root, state, token);
    }

    private static async Task ReplayAsync(ClusterFixture fixture, KeyLoadClient root,
        AdversarialAuthorityState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var rootSession = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, token);
            try
            {
                await AdversarialAuthorityAssertions.ReplayAsync(root, rootSession, state, token);
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<AdversarialAuthoritySeed> SeedAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        var command = scenario.Command(Guid.NewGuid());
        var receipt = await WithRootAsync(fixture, McpCallerProtocol.Node1,
            async root => await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(command, token)));
        MutationReceipt[] expected = [new(AdversarialAuthorityProtocol.PutKind, McpDocumentProtocol.Collection,
            McpDocumentProtocol.Entity, McpCallerProtocol.FirstRevision)];
        await SqlClusterProtocol.EqualAsync(expected, receipt.Mutations.ToArray());
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        return new(scenario, command, receipt);
    }

    internal static async Task<T> WithRootAsync<T>(ClusterFixture fixture, string node,
        Func<KeyLoadClient, Task<T>> operation)
    {
        var failures = new List<Exception>();
        T value = default!;
        try
        {
            using var http = McpCallerHttp.Create(fixture, node);
            try
            {
                value = await operation(new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution()));
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return value;
    }
}
