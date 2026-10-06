using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class AuthorizedQueryScenario
{
    private const string Collection = "query-adapters";
    private const string Canary = "CANARY";

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2), TimeProvider.System);
        var administrator = fixture.Client("node1");
        var partition = new PartitionRef("integration", "database", "query-adapters", Guid.NewGuid().ToString("N"));
        try
        {
            await ConfigureAndSeedAsync(administrator, partition, timeout.Token);
            var client = await CreateReaderAsync(administrator, fixture, timeout.Token);
            var ast = await VerifyQueryAdaptersAsync(client, partition, timeout.Token);
            await VerifyLiveAndErrorPathsAsync(administrator, client, partition, ast, timeout.Token);
        }
        catch (Exception)
        {
            await fixture.SaveFailureDiagnosticsAsync();
            throw;
        }
    }

    private static async Task ConfigureAndSeedAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var definition = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(Collection, ResourceKind.Collection, Collection)
            {
                Indexes = [new("status", ["/status"])],
                FieldPolicies = [new("/secret", "pii")]
            });
        var configureId = Guid.NewGuid();
        Success(await RetryDuringElectionAsync(() => administrator.ConfigureResourceAsync(configureId, definition,
            cancellationToken), cancellationToken));
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, "a", SerializeOrder(1, "open"), 0),
            new PutDocument(Collection, "b", SerializeOrder(2, "open"), 0),
            new PutDocument(Collection, "c", SerializeOrder(3, "closed"), 0)
        ]);
        Success(await RetryDuringElectionAsync(() => administrator.CommitAsync(command, cancellationToken), cancellationToken));
    }

    private static async Task<KeyLoadClient> CreateReaderAsync(KeyLoadClient administrator, ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var principal = new PrincipalRecord("query-reader", "integration",
            [new("database", Collection, Capability.DocumentsRead | Capability.Query | Capability.ChangesRead)], []);
        var principalCommand = Guid.NewGuid();
        Success(await RetryDuringElectionAsync(() => administrator.ConfigurePrincipalAsync(principalCommand, principal,
            cancellationToken), cancellationToken));
        var secret = "query-reader." + Guid.NewGuid().ToString("N");
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        var credential = new ApiKeyRecord("query-reader", principal.Id, digest);
        var keyCommand = Guid.NewGuid();
        Success(await RetryDuringElectionAsync(() => administrator.ConfigureApiKeyAsync(keyCommand, credential,
            cancellationToken), cancellationToken));
        return fixture.Client("node2", secret);
    }

    private static async Task<SelectQuery> VerifyQueryAdaptersAsync(KeyLoadClient client, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var manifest = Success(await client.QueryCapabilitiesAsync(cancellationToken));
        await Assert.That(manifest.AstVersion).IsEqualTo(1);
        await Assert.That(manifest.ReadOnly).IsTrue();
        await Assert.That(manifest.Adapters).Contains("JSON");
        const string sql = "SELECT * FROM \"query-adapters\" q WHERE q.status = 'open' ORDER BY q.number LIMIT 1";
        var first = Success(await client.QueryAsync(new QueryRequest(partition, sql), cancellationToken));
        await Assert.That(first.Rows.Single().EntityId).IsEqualTo("a");
        await Assert.That(first.Rows[0].Json).DoesNotContain(Canary);
        var ast = CreateQueryAst();
        var second = Success(await client.QueryAstAsync(new(partition, ast, Cursor: first.Cursor), cancellationToken));
        await Assert.That(second.Rows.Single().EntityId).IsEqualTo("b");
        await Assert.That(second.Cursor).IsNull();
        await Assert.That(second.Rows[0].Json).DoesNotContain(Canary);
        var typed = Success(await client.QueryAsync(KeyLoadQuery.From<QueryOrder>(partition, Collection, IntegrationClientOptions.Translation())
            .Where(row => row.Status == "open").OrderBy(row => row.Number).Take(1), cancellationToken: cancellationToken));
        await Assert.That(JsonDefaults.Serialize(typed.Rows).SequenceEqual(JsonDefaults.Serialize(first.Rows))).IsTrue();
        return ast;
    }

    private static SelectQuery CreateQueryAst() => new(Collection, null, [new("*", "*")],
        new Comparison(new FieldOperand("/status"), "=", ValueOperand.Create("open")), [new("/number", false)], 1);

    private static string SerializeOrder(decimal number, string status) =>
        Encoding.UTF8.GetString(JsonDefaults.Serialize(new QueryOrder(number, status, Canary)));

    private static async Task VerifyLiveAndErrorPathsAsync(KeyLoadClient administrator, KeyLoadClient client,
        PartitionRef partition, SelectQuery ast, CancellationToken cancellationToken)
    {
        var denied = await client.QueryAstAsync(new(partition,
            ast with { Filter = new Comparison(new FieldOperand("/secret"), "=", ValueOperand.Create(Canary)) }), cancellationToken);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var liveRequest = new AstQueryRequest(partition, ast with { Order = [], Limit = 100 });
        var snapshot = Success(await client.StartLiveQueryAsync(new(liveRequest), cancellationToken));
        await Assert.That(snapshot.Rows.Length).IsEqualTo(2);
        await CommitUpdateAsync(administrator, partition, cancellationToken);
        await VerifyChangesAsync(administrator, client, partition, liveRequest, snapshot.Cursor, cancellationToken);
    }

    private static async Task CommitUpdateAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var update = new CommandRequest(Guid.NewGuid(), partition,
            [new PatchDocument(Collection, "c", [new("/status", PatchKind.Set, "\"open\"")], 1)]);
        Success(await administrator.CommitAsync(update, cancellationToken));
    }

    private static async Task VerifyChangesAsync(KeyLoadClient administrator, KeyLoadClient client, PartitionRef partition,
        AstQueryRequest request, string cursor, CancellationToken cancellationToken)
    {
        var live = Success(await client.ReadLiveQueryAsync(new(request, cursor), cancellationToken));
        await Assert.That(live.Changes.Single().Reference.Id).IsEqualTo("c");
        await Assert.That(live.Changes[0].Row!.Json).DoesNotContain(Canary);
        var feed = Success(await client.ReadChangesAsync(new(partition, Collection), cancellationToken));
        await Assert.That(feed.Changes.Length).IsEqualTo(4);
        await Assert.That(JsonSerializer.Serialize(feed, JsonDefaults.Options)).DoesNotContain(Canary);
        await VerifyMalformedCommandIsNotPublishedAsync(administrator, client, partition, cancellationToken);
    }

    private static async Task VerifyMalformedCommandIsNotPublishedAsync(KeyLoadClient administrator, KeyLoadClient client,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var malformed = new CommandRequest(Guid.NewGuid(), partition, [null!]);
        var rejection = await administrator.CommitAsync(malformed, cancellationToken);
        await Assert.That(rejection.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await Assert.That(Success(await client.ReadChangesAsync(new(partition, Collection), cancellationToken)).Changes.Length)
            .IsEqualTo(4);
        await Assert.That((await administrator.StatusAsync(cancellationToken)).IsSuccess).IsTrue();
    }

    private sealed record QueryOrder(decimal Number, string Status, string Secret);
}
