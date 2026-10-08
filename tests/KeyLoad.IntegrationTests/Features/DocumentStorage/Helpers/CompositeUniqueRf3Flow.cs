using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class CompositeUniqueRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await CompositeUniqueRf3Scenario.CreateAsync(fixture, admin, deadline.Token);
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
        { await ServerFailureObserver.ObserveAsync(() => observer.DisposeAsync().AsTask(), failures); }
        if (mcp is not null)
        { await ServerFailureObserver.ObserveAsync(() => mcp.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<long> MutationsAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CompositeUniqueRf3Scenario s, CancellationToken token)
    {
        var seed = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.First, CompositeUniqueRf3Scenario.Alpha, 0),
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.Second, CompositeUniqueRf3Scenario.AlphaOther, 0)), false, token);
        await ImageAsync(sdk, mcp, s.Partition, seed.Token.Position, CompositeUniqueRf3Scenario.Alpha, 1, token);
        var replace = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.First, CompositeUniqueRf3Scenario.Delta, 1, ExplicitReplacement: true)), true, token);
        await ImageAsync(sdk, mcp, s.Partition, replace.Token.Position, CompositeUniqueRf3Scenario.Delta, 2, token);
        var patch = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new PatchDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.First,
                [new("/label", PatchKind.Set, "\"epsilon\""), new("/rank", PatchKind.Set, "5")], 2)), false, token);
        await ImageAsync(sdk, mcp, s.Partition, patch.Token.Position, CompositeUniqueRf3Scenario.Epsilon, 3, token);
        var conflict = CompositeUniqueRf3Scenario.Command(s.Partition,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.First, CompositeUniqueRf3Scenario.ConflictJson, 3, ExplicitReplacement: true),
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.Third, CompositeUniqueRf3Scenario.ConflictJson, 0));
        await CompositeUniqueRf3Assertions.RejectedCommitAsync(sdk, mcp, conflict, ErrorCode.Conflict, s.Identity.Secret, token);
        await ImageAsync(sdk, mcp, s.Partition, patch.Token.Position, CompositeUniqueRf3Scenario.Epsilon, 3, token);
        var removed = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new DeleteDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.First, 3)), true, token);
        await ImageAsync(sdk, mcp, s.Partition, removed.Token.Position, null, 0, token);
        var reused = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.Third, CompositeUniqueRf3Scenario.Epsilon, 0)), false, token);
        await ImageAsync(sdk, mcp, s.Partition, reused.Token.Position, CompositeUniqueRf3Scenario.Epsilon, 1, token, CompositeUniqueRf3Scenario.Third);
        var other = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Other,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.First, CompositeUniqueRf3Scenario.Epsilon, 0)), true, token);
        await CompositeUniqueRf3Assertions.RowsAsync(sdk, mcp, s.Other, "label = 'epsilon' AND rank = 5", other.Token.Position,
            [new(CompositeUniqueRf3Scenario.First, 1, CompositeUniqueRf3Scenario.Epsilon)], token);
        await ImageAsync(sdk, mcp, s.Partition, other.Token.Position, CompositeUniqueRf3Scenario.Epsilon, 1, token, CompositeUniqueRf3Scenario.Third);
        return other.Token.Position;
    }

    private static async Task ImageAsync(KeyLoadClient sdk, McpOfficialClient mcp, PartitionRef partition,
        long cut, string? firstJson, long revision, CancellationToken token, string firstId = CompositeUniqueRf3Scenario.First)
    {
        QueryRow[] rows = firstJson is null
            ? [new(CompositeUniqueRf3Scenario.Second, 1, CompositeUniqueRf3Scenario.AlphaOther)]
            : [new(firstId, revision, firstJson), new(CompositeUniqueRf3Scenario.Second, 1, CompositeUniqueRf3Scenario.AlphaOther)];
        rows = [.. rows.OrderBy(row => row.EntityId, StringComparer.Ordinal)];
        await CompositeUniqueRf3Assertions.RowsAsync(sdk, mcp, partition, null, cut, rows, token);
        foreach (var tuple in new[] { ("alpha", 1, CompositeUniqueRf3Scenario.Alpha), ("delta", 4, CompositeUniqueRf3Scenario.Delta),
            ("epsilon", 5, CompositeUniqueRf3Scenario.Epsilon), ("private-rollback-canary", 9, CompositeUniqueRf3Scenario.ConflictJson) })
        {
            QueryRow[] members = firstJson == tuple.Item3 ? [new(firstId, revision, firstJson)] : [];
            await CompositeUniqueRf3Assertions.RowsAsync(sdk, mcp, partition,
                "label = '" + tuple.Item1 + "' AND rank = " + tuple.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture), cut, members, token);
        }
        await CompositeUniqueRf3Assertions.RowsAsync(sdk, mcp, partition, "label = 'alpha' AND rank = 2", cut,
            [new(CompositeUniqueRf3Scenario.Second, 1, CompositeUniqueRf3Scenario.AlphaOther)], token);
    }

    private static async Task DenyRestoreAsync(KeyLoadClient admin, McpOfficialClient observer, KeyLoadClient sdk,
        McpOfficialClient mcp, CompositeUniqueRf3Scenario s, long cut, CancellationToken token)
    {
        await s.SetGrantsAsync(admin, Capability.None, checked(s.Identity.Principal.PolicyEpoch + 1), token);
        var query = CompositeUniqueRf3Assertions.Query(s.Partition, "label = 'epsilon' AND rank = 5");
        await CompositeUniqueRf3Assertions.RejectAsync(await sdk.QueryAsync(query, token), ErrorCode.PermissionDenied);
        var deniedQuery = await mcp.CallAsync(McpCallerTools.QueryExecute, query, token);
        await McpCallerAssertions.ErrorAsync(deniedQuery, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(deniedQuery, s.Identity.Secret, CompositeUniqueRf3Scenario.Epsilon);
        await CompositeUniqueRf3Assertions.RejectedCommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.Third, CompositeUniqueRf3Scenario.ConflictJson, 1, ExplicitReplacement: true)),
            ErrorCode.PermissionDenied, s.Identity.Secret, token);
        await ImageAsync(admin, observer, s.Partition, cut, CompositeUniqueRf3Scenario.Epsilon, 1, token, CompositeUniqueRf3Scenario.Third);
        await CompositeUniqueRf3Assertions.RowsAsync(admin, observer, s.Other, null, cut,
            [new(CompositeUniqueRf3Scenario.First, 1, CompositeUniqueRf3Scenario.Epsilon)], token);
        await s.SetGrantsAsync(admin, CompositeUniqueRf3Scenario.Grants, checked(s.Identity.Principal.PolicyEpoch + 2), token);
        var next = await CompositeUniqueRf3Assertions.CommitAsync(sdk, mcp, CompositeUniqueRf3Scenario.Command(s.Partition,
            new PutDocument(CompositeUniqueRf3Scenario.Collection, CompositeUniqueRf3Scenario.Third, CompositeUniqueRf3Scenario.Delta, 1, ExplicitReplacement: true)), true, token);
        await ImageAsync(sdk, mcp, s.Partition, next.Token.Position, CompositeUniqueRf3Scenario.Delta, 2, token, CompositeUniqueRf3Scenario.Third);
        await CompositeUniqueRf3Assertions.RowsAsync(sdk, mcp, s.Other, "label = 'epsilon' AND rank = 5", next.Token.Position,
            [new(CompositeUniqueRf3Scenario.First, 1, CompositeUniqueRf3Scenario.Epsilon)], token);
    }
}
