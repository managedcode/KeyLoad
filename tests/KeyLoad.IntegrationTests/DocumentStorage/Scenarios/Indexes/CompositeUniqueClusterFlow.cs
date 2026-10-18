using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class CompositeUniqueClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await CompositeUniqueClusterScenario.CreateAsync(fixture, admin, deadline.Token);
        var sdk = new KeyLoadClient(http, scenario.Identity.Secret, IntegrationClientOptions.Execution());
        McpOfficialClient? mcp = null;
        McpOfficialClient? observer = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, scenario.Identity.Secret, deadline.Token);
            observer = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
            var cut = await MutationsAsync(sdk, mcp, scenario, deadline.Token);
            await DenyRestoreAsync(admin, observer, sdk, mcp, scenario, cut, deadline.Token);
        }, failures);
        if (observer is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => observer.DisposeAsync().AsTask(), failures);
        }
        if (mcp is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => mcp.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<long> MutationsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CompositeUniqueClusterScenario s, CancellationToken token)
    {
        var seed = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.First, CompositeUniqueClusterScenario.Alpha, 0),
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.Second, CompositeUniqueClusterScenario.AlphaOther, 0)), false, token);
        await ImageAsync(sdk, mcp, s.Partition, seed.Token.Position, CompositeUniqueClusterScenario.Alpha, 1, token);
        var replace = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.First, CompositeUniqueClusterScenario.Delta, 1, ExplicitReplacement: true)), true, token);
        await ImageAsync(sdk, mcp, s.Partition, replace.Token.Position, CompositeUniqueClusterScenario.Delta, 2, token);
        var patch = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new PatchDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.First,
                [new("/label", PatchKind.Set, "\"epsilon\""), new("/rank", PatchKind.Set, "5")], 2)), false, token);
        await ImageAsync(sdk, mcp, s.Partition, patch.Token.Position, CompositeUniqueClusterScenario.Epsilon, 3, token);
        var conflict = CompositeUniqueClusterScenario.Command(s.Partition,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.First, CompositeUniqueClusterScenario.ConflictJson, 3, ExplicitReplacement: true),
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.Third, CompositeUniqueClusterScenario.ConflictJson, 0));
        await CompositeUniqueClusterAssertions.RejectedCommitAsync(sdk, mcp, conflict, ErrorCode.Conflict, s.Identity.Secret, token);
        await ImageAsync(sdk, mcp, s.Partition, patch.Token.Position, CompositeUniqueClusterScenario.Epsilon, 3, token);
        var removed = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new DeleteDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.First, 3)), true, token);
        await ImageAsync(sdk, mcp, s.Partition, removed.Token.Position, null, 0, token);
        var reused = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.Third, CompositeUniqueClusterScenario.Epsilon, 0)), false, token);
        await ImageAsync(sdk, mcp, s.Partition, reused.Token.Position, CompositeUniqueClusterScenario.Epsilon, 1, token, CompositeUniqueClusterScenario.Third);
        var other = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Other,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.First, CompositeUniqueClusterScenario.Epsilon, 0)), true, token);
        await CompositeUniqueClusterAssertions.RowsAsync(sdk, mcp, s.Other, "label = 'epsilon' AND rank = 5", other.Token.Position,
            [new(CompositeUniqueClusterScenario.First, 1, CompositeUniqueClusterScenario.Epsilon)], token);
        await ImageAsync(sdk, mcp, s.Partition, other.Token.Position, CompositeUniqueClusterScenario.Epsilon, 1, token, CompositeUniqueClusterScenario.Third);
        return other.Token.Position;
    }

    private static async Task ImageAsync(KeyLoadClient sdk, McpOfficialClient mcp, PartitionRef partition,
        long cut, string? firstJson, long revision, CancellationToken token, string firstId = CompositeUniqueClusterScenario.First)
    {
        QueryRow[] rows = firstJson is null
            ? [new(CompositeUniqueClusterScenario.Second, 1, CompositeUniqueClusterScenario.AlphaOther)]
            : [new(firstId, revision, firstJson), new(CompositeUniqueClusterScenario.Second, 1, CompositeUniqueClusterScenario.AlphaOther)];
        rows = [.. rows.OrderBy(row => row.EntityId, StringComparer.Ordinal)];
        await CompositeUniqueClusterAssertions.RowsAsync(sdk, mcp, partition, null, cut, rows, token);
        foreach (var tuple in new[] { ("alpha", 1, CompositeUniqueClusterScenario.Alpha), ("delta", 4, CompositeUniqueClusterScenario.Delta),
            ("epsilon", 5, CompositeUniqueClusterScenario.Epsilon), ("private-rollback-canary", 9, CompositeUniqueClusterScenario.ConflictJson) })
        {
            QueryRow[] members = firstJson == tuple.Item3 ? [new(firstId, revision, firstJson)] : [];
            await CompositeUniqueClusterAssertions.RowsAsync(sdk, mcp, partition,
                "label = '" + tuple.Item1 + "' AND rank = " + tuple.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture), cut, members, token);
        }
        await CompositeUniqueClusterAssertions.RowsAsync(sdk, mcp, partition, "label = 'alpha' AND rank = 2", cut,
            [new(CompositeUniqueClusterScenario.Second, 1, CompositeUniqueClusterScenario.AlphaOther)], token);
    }

    private static async Task DenyRestoreAsync(KeyLoadClient admin, McpOfficialClient observer, KeyLoadClient sdk,
        McpOfficialClient mcp, CompositeUniqueClusterScenario s, long cut, CancellationToken token)
    {
        await s.SetGrantsAsync(admin, Capability.None, checked(s.Identity.Principal.PolicyEpoch + 1), token);
        var query = CompositeUniqueClusterAssertions.Query(s.Partition, "label = 'epsilon' AND rank = 5");
        await CompositeUniqueClusterAssertions.RejectAsync(await sdk.QueryAsync(query, token), ErrorCode.PermissionDenied);
        var deniedQuery = await mcp.CallAsync(McpCallerTools.QueryExecute, query, token);
        await McpCallerAssertions.ErrorAsync(deniedQuery, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(deniedQuery, s.Identity.Secret, CompositeUniqueClusterScenario.Epsilon);
        await CompositeUniqueClusterAssertions.RejectedCommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.Third, CompositeUniqueClusterScenario.ConflictJson, 1, ExplicitReplacement: true)),
            ErrorCode.PermissionDenied, s.Identity.Secret, token);
        await ImageAsync(admin, observer, s.Partition, cut, CompositeUniqueClusterScenario.Epsilon, 1, token, CompositeUniqueClusterScenario.Third);
        await CompositeUniqueClusterAssertions.RowsAsync(admin, observer, s.Other, null, cut,
            [new(CompositeUniqueClusterScenario.First, 1, CompositeUniqueClusterScenario.Epsilon)], token);
        await s.SetGrantsAsync(admin, CompositeUniqueClusterScenario.Grants, checked(s.Identity.Principal.PolicyEpoch + 2), token);
        var next = await CompositeUniqueClusterAssertions.CommitAsync(sdk, mcp, CompositeUniqueClusterScenario.Command(s.Partition,
            new PutDocument(CompositeUniqueClusterScenario.Collection, CompositeUniqueClusterScenario.Third, CompositeUniqueClusterScenario.Delta, 1, ExplicitReplacement: true)), true, token);
        await ImageAsync(sdk, mcp, s.Partition, next.Token.Position, CompositeUniqueClusterScenario.Delta, 2, token, CompositeUniqueClusterScenario.Third);
        await CompositeUniqueClusterAssertions.RowsAsync(sdk, mcp, s.Other, "label = 'epsilon' AND rank = 5", next.Token.Position,
            [new(CompositeUniqueClusterScenario.First, 1, CompositeUniqueClusterScenario.Epsilon)], token);
    }
}
