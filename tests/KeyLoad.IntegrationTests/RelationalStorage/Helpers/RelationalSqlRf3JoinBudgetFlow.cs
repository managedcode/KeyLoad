using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

using Q2AdmissionRouting = global::KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Runs one rejection and healthy follow-up against an owned real Aspire RF3 fixture.</summary>
internal static class RelationalSqlRf3JoinBudgetFlow
{
    private const string NodeForSdk = McpCallerProtocol.Node1;
    private const string NodeForMcp = McpCallerProtocol.Node2;
    private const string OutputCustomerId = "customer_id";
    private const string OutputDetail = "left_detail";
    private const string OutputName = "right_name";
    private const string OutputOrderId = "order_id";
    private const string JoinAccessPath = "bounded-primary-key-inner-join";
    private const int InitialSeedIndex = 1;
    private const long SeedRevision = 1;
    private const string MissingDocument = "The SDK did not return a seeded join source.";

    internal static async Task ExecuteAsync(JoinBudgetKind kind, DatabaseLimits limits, QueryExecutionOptions? queryExecution = null)
    {
        var fixture = queryExecution is null ? new ClusterFixture(limits) : new ClusterFixture(limits, queryExecution);
        try
        {
            await fixture.InitializeAsync();
            await RunFixtureAsync(fixture, kind);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static async Task RunFixtureAsync(ClusterFixture fixture, JoinBudgetKind kind)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, NodeForSdk);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var rejected = await PrepareAsync(sdk, kind, false, deadline.Token);
        var healthy = await PrepareAsync(sdk, kind, true, deadline.Token);
        var before = await SnapshotAsync(sdk, rejected.Sources, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, NodeForMcp,
            fixture.AdminKey, deadline.Token);

        await RejectBothCallersAsync(sdk, mcp, rejected.Partition, deadline.Token);
        var after = await SnapshotAsync(sdk, rejected.Sources, deadline.Token);
        await AssertUnchangedAsync(before, after);
        await VerifyHealthyBothCallersAsync(sdk, mcp, healthy, deadline.Token);
    }

    private static async Task<JoinBudgetSeed> PrepareAsync(KeyLoadClient sdk, JoinBudgetKind kind,
        bool healthy, CancellationToken token)
    {
        var partition = RelationalSqlRf3JoinBudgetData.Partition(healthy ? "healthy" : "rejected");
        await RelationalSqlRf3JoinBudgetData.ConfigureAsync(sdk, partition, token);
        return await RelationalSqlRf3JoinBudgetData.SeedAsync(sdk, partition, kind, healthy, token);
    }

    private static async Task RejectBothCallersAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken token)
    {
        var request = RelationalSqlRf3JoinBudgetData.Query(partition);
        var sdkFailure = await sdk.QueryAsync(request, token);
        await Assert.That(sdkFailure.IsSuccess).IsFalse();
        await Assert.That(sdkFailure.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        var mcpFailure = await mcp.CallAsync(McpCallerTools.QueryExecute, request, token);
        await McpCallerAssertions.ErrorAsync(mcpFailure, ErrorCode.BudgetExceeded, dispatched: true);
    }

    private static async Task VerifyHealthyBothCallersAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        JoinBudgetSeed healthy, CancellationToken token)
    {
        var request = RelationalSqlRf3JoinBudgetData.Query(healthy.Partition);
        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(request, token));
        var mcpPage = (await McpCallerAssertions.SuccessAsync<QueryPage>(
            await mcp.CallAsync(McpCallerTools.QueryExecute, request, token))).Value;
        await RelationalSqlRf3JoinPageAssertions.EquivalentAsync(sdkPage, mcpPage);
        await VerifyHealthyResultAsync(sdkPage, healthy);
    }

    private static async Task VerifyHealthyResultAsync(QueryPage page, JoinBudgetSeed healthy)
    {
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(healthy.CommitPosition);
        await Assert.That(page.AccessPath).IsEqualTo(JoinAccessPath);
        await Assert.That(page.Rows[0].EntityId).IsEqualTo(healthy.ExpectedOrderId);
        await RelationalSqlRf3JoinPageAssertions.RevisionsAsync(page, SeedRevision, SeedRevision);
        using var row = JsonDocument.Parse(page.Rows[0].Json);
        await Assert.That(row.RootElement.GetProperty(OutputOrderId).GetString()).IsEqualTo(healthy.ExpectedOrderId);
        await Assert.That(row.RootElement.GetProperty(OutputDetail).GetString()).IsEqualTo(healthy.ExpectedDetail);
        await Assert.That(row.RootElement.GetProperty(OutputName).GetString()).IsEqualTo(healthy.ExpectedName);
        await Assert.That(row.RootElement.GetProperty(OutputCustomerId).GetString())
            .IsEqualTo(healthy.ExpectedCustomerId);
    }

    private static async Task<ImmutableArray<StoredSource>> SnapshotAsync(KeyLoadClient sdk,
        ImmutableArray<EntityRef> references, CancellationToken token)
    {
        var snapshots = ImmutableArray.CreateBuilder<StoredSource>(references.Length);
        foreach (var reference in references)
        {
            var document = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, token));
            var stored = document ?? throw new InvalidOperationException(MissingDocument);
            await Assert.That(stored.Reference).IsEqualTo(reference);
            snapshots.Add(new(stored.Reference, stored.Revision, stored.Json));
        }
        return snapshots.MoveToImmutable();
    }

    private static async Task AssertUnchangedAsync(ImmutableArray<StoredSource> before,
        ImmutableArray<StoredSource> after)
    {
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = InitialSeedIndex - 1; index < before.Length; index++)
        {
            await Assert.That(after[index].Reference).IsEqualTo(before[index].Reference);
            await Assert.That(after[index].Revision).IsEqualTo(before[index].Revision);
            await Assert.That(after[index].Json).IsEqualTo(before[index].Json);
        }
    }

    private sealed record StoredSource(EntityRef Reference, long Revision, string Json);
}

internal sealed class RelationalSqlRf3JoinAdmissionFlow(bool officialMcp, bool ast)
{
    private const string MissingOwner = "The original Q2 admission owner was not retained.";
    private string root = string.Empty;
    private bool rootOwned;
    private bool waveStartupAttempted;
    private Q2AdmissionRouting.RequestCqrsProbeFixture? controls;
    private Q2AdmissionRouting.RequestCqrsRf3Wave? wave;
    private Q2AdmissionRouting.RequestCqrsRf3Callers? administrator;
    private Q2AdmissionRouting.RequestCqrsRf3Callers? caller;
    private IReadOnlyList<global::KeyLoad.Clustering.ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? callerCancellation;
    private Task<RelationalQ2CancellationOutcome>? original;
    private Guid armId;
    private readonly List<Exception> failures = [];

    internal static async Task RunAsync(bool officialMcp, bool ast, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(Q2AdmissionRouting.RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var flow = new RelationalSqlRf3JoinAdmissionFlow(officialMcp, ast);
        await Q2AdmissionRouting.Helpers.RequestCqrsLifecycleFailureObserver.ObserveAsync(
            () => flow.ExecuteAsync(parent.Token), flow.failures, null,
            Q2AdmissionRouting.RequestCqrsLifecycleStage.FaultProfile);
        await Q2AdmissionRouting.RequestCqrsPhaseFaultCleanup.RunAsync(flow.root, flow.rootOwned,
            flow.controls, flow.wave, flow.waveStartupAttempted, flow.caller, flow.administrator,
            flow.discovery, flow.callerCancellation, null, flow.original, null, flow.armId, flow.failures);
        if (flow.failures.Count == 1)
        {
            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(flow.failures[0]).Throw();
        }
        if (flow.failures.Count > 1)
        {
            throw new AggregateException(flow.failures);
        }
    }

    private async Task ExecuteAsync(CancellationToken parent)
    {
        using var startupTimeout = new CancellationTokenSource(Q2AdmissionRouting.RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(parent, startupTimeout.Token);
        var seed = await PrepareAsync(startup.Token);
        using var deadline = McpCallerDeadline.Create();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(parent, deadline.Token);
        var admin = administrator ?? throw new InvalidOperationException(MissingOwner);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingOwner);
        var before = await RelationalSqlRf3JoinAdmissionAssertions.CaptureAsync(admin.Sdk, seed, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.HealthyAsync(activeCaller, seed, bounded.Token);
        await HoldCancelAndJoinAsync(seed, before, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(admin.Sdk, seed, before, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.HealthyAsync(activeCaller, seed, bounded.Token);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(admin.Sdk, seed, before, bounded.Token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(seed.Command, bounded.Token));
        await SqlRf3Protocol.EqualAsync(seed.Receipt, replay);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(admin.Sdk, seed, before, bounded.Token);
    }

    private async Task<RelationalQ2AdmissionSeed> PrepareAsync(CancellationToken token)
    {
        root = Q2AdmissionRouting.RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        Q2AdmissionRouting.RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        var dataRoot = Path.Combine(root, "data");
        var profile = (await global::KeyLoad.IntegrationTests.Features.StorageRecovery.NodeEpochRf3Profile.CreatePriorAsync(dataRoot, token)).Profile;
        var image = await Q2AdmissionRouting.RequestCqrsRf3ImageProof.ReadAsync(token);
        controls = Q2AdmissionRouting.RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        waveStartupAttempted = true;
        wave = await Q2AdmissionRouting.RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            Q2AdmissionRouting.RequestCqrsPhaseFaultProvisioning.CurrentImages(image.Current), controls, token);
        discovery = await Q2AdmissionRouting.RequestCqrsAuthorityFaultDiscovery.ReadAsync(wave.App, profile, token);
        administrator = await Q2AdmissionRouting.RequestCqrsRf3Callers.ConnectAsync(wave.App,
            Q2AdmissionRouting.RequestCqrsRf3Protocol.Node1, profile.AdminKey, token);
        var seed = await RelationalSqlRf3JoinAdmissionSeed.CreateAsync(administrator.Sdk, token);
        caller = await Q2AdmissionRouting.RequestCqrsRf3Callers.ConnectAsync(wave.App,
            Q2AdmissionRouting.RequestCqrsRf3Protocol.Node2, seed.Identity.Secret, token);
        return seed;
    }

    private async Task HoldCancelAndJoinAsync(RelationalQ2AdmissionSeed seed, string[] before, CancellationToken token)
    {
        var activeControls = controls ?? throw new InvalidOperationException(MissingOwner);
        var actualDiscovery = discovery ?? throw new InvalidOperationException(MissingOwner);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingOwner);
        var kind = ast ? global::KeyLoad.Clustering.GrainReadKind.AstQuery : global::KeyLoad.Clustering.GrainReadKind.Query;
        armId = activeControls.WriteArm(seed.Identity.Principal.Id, Guid.Empty, kind,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeAction.Hold);
        callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        await Assert.That(callerCancellation.IsCancellationRequested).IsFalse();
        original = RelationalSqlRf3JoinAdmissionOperation.StartAsync(activeCaller, seed,
            officialMcp, ast, callerCancellation.Token);
        var observed = await activeControls.WaitForMarkerAsync(armId,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Observed, actualDiscovery, token);
        await Assert.That(original.IsCompleted).IsFalse();
        await Assert.That(observed.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(observed.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(observed.ArmId).IsEqualTo(armId);
        await RelationalSqlRf3JoinAdmissionAssertions.UnchangedAsync(
            (administrator ?? throw new InvalidOperationException(MissingOwner)).Sdk, seed, before, token);
        await Assert.That(original.IsCompleted).IsFalse();
        await callerCancellation.CancelAsync();
        var cancelled = await activeControls.WaitForMarkerAsync(armId,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.AuthorizationReload,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Cancelled, actualDiscovery, token);
        await SqlRf3Protocol.EqualAsync(observed with
        {
            Outcome = global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Cancelled
        }, cancelled);
        var disposed = await activeControls.WaitForMarkerAsync(armId,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.ProducerDisposed,
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbeOutcome.Observed, actualDiscovery, token);
        await SqlRf3Protocol.EqualAsync(observed with
        {
            Phase = global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsProbePhase.ProducerDisposed
        }, disposed);
        await Assert.That(activeControls.ArmFor(armId).Settled).IsTrue();
        await Assert.That(activeControls.ArmFor(armId).ProducerDisposedSeen).IsTrue();
        var outcome = await original;
        await Assert.That(callerCancellation.IsCancellationRequested).IsTrue();
        await RelationalSqlRf3JoinAdmissionAssertions.CancelledAsync(outcome, officialMcp);
        await activeControls.RetireArmAsync(armId, token);
    }
}

internal sealed record RelationalQ2CancellationOutcome(bool ReturnedSuccess, string? ErrorCode,
    OperationCanceledException? Cancellation);

internal static class RelationalSqlRf3JoinAdmissionOperation
{
    internal static async Task<RelationalQ2CancellationOutcome> StartAsync(
        Q2AdmissionRouting.RequestCqrsRf3Callers caller, RelationalQ2AdmissionSeed seed,
        bool officialMcp, bool ast, CancellationToken token)
    {
        try
        {
            if (officialMcp)
            {
                if (ast)
                {
                    await caller.Mcp.CallAsync(McpCallerTools.QueryAst, seed.Ast, token);
                }
                else
                {
                    await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, seed.Sql, token);
                }
                return new(true, null, null);
            }
            if (ast)
            {
                var result = await caller.Sdk.QueryAstAsync(seed.Ast, token);
                await Assert.That(result.Value).IsNull();
                return new(result.IsSuccess, result.Problem?.ErrorCode, null);
            }
            var sql = await caller.Sdk.ExecuteSqlAsync(seed.Sql, token);
            await Assert.That(sql.Value).IsNull();
            return new(sql.IsSuccess, sql.Problem?.ErrorCode, null);
        }
        catch (OperationCanceledException cancellation) when (token.IsCancellationRequested)
        {
            return new(false, null, cancellation);
        }
    }
}
