using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses the actual six Server owners and native ConnectionGrain private Parent; no direct executor.</summary>
internal static class D3PrivateRetirementPolicyFlow
{
    private const string Root = "root";
    private const string RepairPrincipal = "d3-boundary-repair";
    private const string Collection = "d3-boundary-docs";
    private const string DocumentId = "healthy";
    private const string Json = "{\"value\":93}";
    private const long FirstRevision = 1;
    private const int Version = 1;
    private const int MetadataShareDivisor = 2;
    private const int NoFailures = 0;
    private const long EpochStep = 1;
    private const long NoReadBytes = 0;
    private const int NoReadRecords = 0;
    private const string Required = "The actual private retirement policy fixture is incomplete.";

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        CommandObservedPolicyRuntime? runtime = null;
        ReadExecutionBudget? work = null;
        ReadExecutionBudgetReadGrant? metadata = null;
        try
        {
            runtime = await CommandObservedPolicyRuntime.CreateAsync(new ConnectionOperationObservation(), token);
            var source = runtime.Source;
            work = new(source.Database.OperationLimitsOptions, source.Clock, token);
            var expiry = source.Clock.GetUtcNow() + source.Silo.CatalogRequestCodec().ExecutionOptions.Value.RequestLifetime;
            work.ConstrainLifetime(expiry);
            metadata = work.CreateReadGrant(work.RemainingReadGrantBytes / MetadataShareDivisor,
                work.RemainingReadGrantRecords / MetadataShareDivisor);
            await ExecuteAsync(runtime, work, metadata, expiry, token);
        }
        catch (Exception original)
        {
            failures.Add(original);
        }
        // Every owned native call and its request-context disposal is awaited inline before this settlement.
        if (work is not null && metadata is not null)
        {
            ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(metadata), failures);
        }

        if (runtime is not null)
        {
            if (failures.Count != NoFailures)
            {
                runtime.RetainRoots();
            }

            await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(CommandObservedPolicyRuntime runtime, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadata, DateTimeOffset expiry, CancellationToken token)
    {
        var partition = runtime.Database.Partition;
        var source = runtime.Source;
        var definition = CommandObservedPolicyAssertions.Value<ResourceDefinition>(await runtime.Database.SubmitAsync(
            OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(Collection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var principal = source.Store.Read(view => source.Database.Principal(work.CreateView(view, metadata),
            Root, source.Clock.GetUtcNow()));
        await Assert.That((await runtime.Database.SubmitAsync(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal with
            {
                Id = RepairPrincipal,
                OwnerId = RepairPrincipal
            }), token)).Error).IsNull();
        var lifetime = source.Store.Read(view => ResourceLifetimeCatalog.Read(work.CreateView(view, metadata),
            partition.TenantId, partition.DatabaseId, Collection, definition)) ?? throw new InvalidOperationException(Required);
        var request = new ResourceRetirementRequestV1(Version, Guid.NewGuid(), partition.TenantId,
            partition.DatabaseId, Collection, lifetime.Incarnation);
        var original = D3PrivateRetirementParentCalls.Issue(source, request, Root, work, metadata);
        await Assert.That((await runtime.Database.SubmitAsync(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal with
            {
                ClusterAdministrator = false,
                PolicyEpoch = checked(principal.PolicyEpoch + EpochStep)
            }), token)).Error).IsNull();
        await runtime.Database.Coordinator.ReadBarrierAsync(token);
        var before = D3PrivateRetirementParentImage.Capture(source);
        var bytes = work.ReadBytes;
        var reply = await D3PrivateRetirementParentCalls.ExecuteAsync(source, original, expiry, work, metadata, token);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(reply.SafeDetail).IsEqualTo(GrainRoutingProtocol.AdministrationRequired);
        await Assert.That(reply.Metadata!.ReadBytes).IsGreaterThan(NoReadBytes);
        await Assert.That(reply.Metadata.ExaminedRecords).IsGreaterThan(NoReadRecords);
        await Assert.That(work.ReadBytes).IsGreaterThan(bytes);
        await before.UnchangedAsync(source);
        await Assert.That((await runtime.Database.SubmitAsync(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal with
            {
                PolicyEpoch = checked(principal.PolicyEpoch + EpochStep + EpochStep)
            }),
            token, principal: RepairPrincipal)).Error).IsNull();
        await runtime.Database.Coordinator.ReadBarrierAsync(token);
        await HealthyAsync(runtime, request with
        {
            RetirementId = Guid.NewGuid()
        }, expiry, work, metadata, token);
        await OrdinaryHealthyAsync(runtime, work, metadata, token);
    }

    private static async Task OrdinaryHealthyAsync(CommandObservedPolicyRuntime runtime,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, runtime.Database.Partition,
            [new PutDocument(Collection, DocumentId, Json)]);
        var reply = await D3PrivateRetirementParentCalls.ExecuteOrdinaryAsync(runtime.Source,
            Root, command, work, metadata, token);
        await Assert.That(reply.Error).IsNull();
        var receipt = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as CommitReceipt
            ?? throw new InvalidOperationException(Required);
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(Version);
        await runtime.Database.Coordinator.ReadBarrierAsync(token);
        var document = runtime.Source.Database.GetDocument(Root,
            new(runtime.Database.Partition, Collection, DocumentId), cancellationToken: token)
            ?? throw new InvalidOperationException(Required);
        await Assert.That(document.Json).IsEqualTo(Json);
        await Assert.That(document.Revision).IsEqualTo(FirstRevision);
        await Assert.That(document.Redacted).IsFalse();
    }

    private static async Task HealthyAsync(CommandObservedPolicyRuntime runtime, ResourceRetirementRequestV1 request,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var original = D3PrivateRetirementParentCalls.Issue(runtime.Source, request, Root, work, metadata);
        var bytes = work.ReadBytes;
        var reply = await D3PrivateRetirementParentCalls.ExecuteAsync(runtime.Source, original, expiry, work, metadata, token);
        await Assert.That(reply.Error).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as ResourceRetirementPhaseResultV1
            ?? throw new InvalidOperationException(Required);
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(original.Id);
        await Assert.That(actual.PreparationState).IsNotNull();
        await CommandObservedPolicyAssertions.EqualAsync(request, actual.PreparationState!.Control.Request);
        await Assert.That(actual.PreparationState.Control.PrincipalId).IsEqualTo(Root);
        await Assert.That(work.ReadBytes).IsGreaterThan(bytes);
        await Assert.That(reply.Metadata!.ReadBytes).IsGreaterThan(NoReadBytes);
    }
}
