using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Search;

/// <summary>AC-FILTER-001/002/003 and F1 of AC-FILTER-005: public Orleans allowlist behavior.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class FilteredSearchClusterTests(ClusterFixture fixture)
{
    private const string Needle = "alpha";
    private const string Fresh = "fresh";
    private const double FirstSingleScore = 1d / 61d;
    private const double SecondSingleScore = 1d / 62d;
    private const double FirstHybridScore = 2d / 61d;

    [Test]
    public async Task AcFilter001NullEmptyDuplicatesUnknownIdsAndBranchRanksMatchIndependentOracle()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await FilteredSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, null, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);

        await VerifyBothAsync(sdk, mcp, scenario.Hybrid(Needle),
        [
            ("a", FirstHybridScore), ("b", 2d / 62d), ("d", 1d / 63d), ("c", 1d / 64d)
        ], deadline.Token);
        var allowed = ImmutableArray.Create("b", "d", "unknown", "b");
        await VerifyBothAsync(sdk, mcp, scenario.Text(Needle, allowed), [("b", FirstSingleScore)], deadline.Token);
        await VerifyBothAsync(sdk, mcp, scenario.Vector(allowed),
            [("b", FirstSingleScore), ("d", SecondSingleScore)], deadline.Token);
        await VerifyBothAsync(sdk, mcp, scenario.Hybrid(Needle, allowed),
            [("b", FirstHybridScore), ("d", SecondSingleScore)], deadline.Token);
        await VerifyBothAsync(sdk, mcp, scenario.Hybrid(Needle, ImmutableArray<string>.Empty), [], deadline.Token);
    }

    [Test]
    public async Task AcFilter002HiddenDeletedStaleAndCurrentWritesUseLiveEligibility()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await FilteredSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var restricted = await scenario.CreateReaderAsync(fixture, true, true, true,
            FilteredSearchClusterScenario.OwnerA, deadline.Token);
        using var callerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var caller = new KeyLoadClient(callerHttp, restricted.Secret, IntegrationClientOptions.Execution());
        var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            restricted.Secret, deadline.Token);
        var visible = ImmutableArray.Create("a", "b", "missing", "b");
        await VerifyBothAsync(caller, mcp, scenario.Hybrid(Needle, visible), [("a", 2d / 61d)], deadline.Token);

        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [
            new PutDocument(FilteredSearchClusterScenario.Collection, "a", "{\"text\":\"fresh wording\"}",
                FilteredSearchClusterScenario.FirstRevision, new(FilteredSearchClusterScenario.OwnerA)),
            new DeleteDocument(FilteredSearchClusterScenario.Collection, "b", FilteredSearchClusterScenario.FirstRevision)
        ]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(update, deadline.Token));
        await VerifyBothAsync(caller, mcp, scenario.Text(Needle, visible), [], deadline.Token);
        await VerifyBothAsync(caller, mcp, scenario.Text(Fresh, visible), [("a", FirstSingleScore)], deadline.Token);
        await VerifyBothAsync(caller, mcp, scenario.Vector(ImmutableArray.Create("a")), [], deadline.Token);

        var refreshed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
        [new PutVector(FilteredSearchClusterScenario.Collection, "a", FilteredSearchClusterScenario.VectorField,
            [1, 0], FilteredSearchClusterScenario.Space, FilteredSearchClusterScenario.SecondRevision)]);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(refreshed, deadline.Token));
        await VerifyBothAsync(caller, mcp, scenario.Vector(ImmutableArray.Create("a")),
            [("a", FirstSingleScore)], deadline.Token);
    }

    [Test]
    public async Task AcFilter002PersistedFieldGrantIsRequiredEvenForAnEmptyAllowlist()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await FilteredSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, false, true, false, null, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var request = scenario.Text(Needle, ImmutableArray<string>.Empty);
        var denied = await sdk.SearchAsync(request, deadline.Token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SearchExecute,
            request, deadline.Token), ErrorCode.PermissionDenied, dispatched: true);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Vector(ImmutableArray.Create("a")), deadline.Token));
        await Assert.That(healthy.Select(item => item.Document.Reference.Id).ToArray()).IsEquivalentTo(["a"]);
    }

    [Test]
    public async Task AcFilter003OversizedAllowlistAndCancellationFailWithoutPartialResults()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await FilteredSearchClusterScenario.CreateAsync(fixture, deadline.Token);
        var identity = await scenario.CreateReaderAsync(fixture, true, true, false, null, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var oversized = Enumerable.Range(0, 10_001).Select(index => "record-" + index.ToString(
            System.Globalization.CultureInfo.InvariantCulture)).ToImmutableArray();
        var request = scenario.Vector(oversized);
        var rejected = await sdk.SearchAsync(request, deadline.Token);
        await Assert.That(rejected.IsFailed).IsTrue();
        await Assert.That(rejected.Problem?.ErrorCode).IsEqualTo(ErrorCode.BudgetExceeded.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SearchExecute,
            request, deadline.Token), ErrorCode.BudgetExceeded, dispatched: true);
        var resultLimit = scenario.Vector(ImmutableArray.Create("a")) with
        {
            Limit = 1_001
        };
        await AssertSdkFailureAsync(await sdk.SearchAsync(resultLimit, deadline.Token), ErrorCode.Validation);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SearchExecute,
            resultLimit, deadline.Token), ErrorCode.Validation, dispatched: true);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var healthyRequest = scenario.Vector(ImmutableArray.Create("a"));
        await AssertSdkFailureAsync(await sdk.SearchAsync(healthyRequest, cancelled.Token), ErrorCode.Cancelled);
        await Assert.ThrowsAsync<OperationCanceledException>(() => mcp.CallAsync(McpCallerTools.SearchExecute,
            healthyRequest, cancelled.Token));
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(
            scenario.Vector(ImmutableArray.Create("a")), deadline.Token));
        await Assert.That(healthy).HasSingleItem();
        await Assert.That(healthy[0].Document.Reference.Id).IsEqualTo("a");
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, scenario.Vector(ImmutableArray.Create("a")), deadline.Token));
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
    }

    private static async Task VerifyBothAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SearchRequest request, (string Id, double Score)[] expected, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, request, cancellationToken));
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
        }
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
    }

    private static async Task AssertSdkFailureAsync<T>(Result<T> result, ErrorCode code)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
    }
}
