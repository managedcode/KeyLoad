using KeyLoad.Clustering;
using KeyLoad.Server;
using L = KeyLoad.FunctionalTests.RelationalStorage.SqlCreateTableLiteral;

namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Preserves the administrator law through the public command envelope and production Orleans hosts.</summary>
internal static class ResourceAdministrationFlow
{
    private const string Root = "root";
    private const string RepairPrincipal = "execution-administration-repair";
    private const string Collection = "execution-administration-documents";
    private const string DocumentId = "healthy";
    private const string Json = "{\"value\":93}";
    private const int SourceIndex = 0;
    private const int TerminalSequence = 2;
    private const int PersistedCopies = 2;
    private const int LocalPersistedCopies = 1;
    private const int NoFailures = 0;
    private const long FirstRevision = 1;
    private const long EpochStep = 1;

    internal static Task RunAsync(CancellationToken token)
        => RequestCqrsHostedCluster.RunAsync(async cluster =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(cluster, token), failures);
            if (failures.Count != NoFailures)
            {
                cluster.RetainRoots();
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }, token);

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, CancellationToken token)
    {
        var node = cluster.Nodes[SourceIndex];
        var source = node.Database;
        var principal = source.Store.Read(view => source.Database.Principal(view, Root, node.Clock.GetUtcNow()));
        await PrepareAsync(node, principal, token);
        var partition = source.Partition;
        var request = new DropTableRequest(partition.TenantId, partition.DatabaseId, L.Table);
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var envelope = node.Codec.CreateCommand(requestId, principal.Id, OperationKind.DropTable, commandId,
            request);
        var revoked = principal with
        {
            ClusterAdministrator = false,
            PolicyEpoch = checked(principal.PolicyEpoch + EpochStep)
        };
        await CommandObservedPolicyAssertions.EqualAsync(revoked, source.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(revoked), cancellationToken: token).Get<PrincipalRecord>());
        await RefusedAsync(cluster, node, principal, envelope, requestId, commandId, token);
        var repaired = principal with
        {
            PolicyEpoch = checked(revoked.PolicyEpoch + EpochStep)
        };
        await CommandObservedPolicyAssertions.EqualAsync(repaired, source.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(repaired), RepairPrincipal, cancellationToken: token).Get<PrincipalRecord>());
        var healthyRequestId = Guid.NewGuid();
        var healthySigned = node.Codec.CreateCommand(healthyRequestId, repaired.Id, OperationKind.DropTable, commandId,
            request);
        var reply = await RequestCqrsCommandStream.InvokeAsync(node, healthySigned, repaired,
            healthyRequestId, commandId, TerminalSequence);
        var actual = CommandObservedPolicyAssertions.Value<DropTableResult>(reply);
        await Assert.That(actual.TenantId).IsEqualTo(request.TenantId);
        await Assert.That(actual.DatabaseId).IsEqualTo(request.DatabaseId);
        await Assert.That(actual.ResourceName).IsEqualTo(request.ResourceName);
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.Receipt.PersistedCopies).IsEqualTo(PersistedCopies);
        await Assert.That(actual.Receipt.Mutations.IsEmpty).IsTrue();
        await HealthyAsync(cluster, request, commandId, actual, token);
    }

    private static async Task PrepareAsync(RequestCqrsHostedRuntimeFixture node, PrincipalRecord principal,
        CancellationToken token)
    {
        var source = node.Database;
        var repair = principal with
        {
            Id = RepairPrincipal,
            OwnerId = RepairPrincipal
        };
        await CommandObservedPolicyAssertions.EqualAsync(repair, source.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(repair), cancellationToken: token).Get<PrincipalRecord>());
        var definition = L.Definition(source.Partition);
        await CommandObservedPolicyAssertions.EqualAsync(definition, source.Submit(OperationKind.CreateTable,
            new CreateTableRequest(source.Partition.TenantId, source.Partition.DatabaseId, definition),
            cancellationToken: token).Get<ResourceDefinition>());
        _ = source.Configure(Collection, ResourceKind.Collection);
        var seed = source.Commit(new PutDocument(L.Table, L.One, L.Row));
        await Assert.That(seed.PersistedCopies).IsEqualTo(PersistedCopies);
        await Assert.That(source.Database.GetDocument(Root, new(source.Partition, L.Table, L.One),
            cancellationToken: token)!.Json).IsEqualTo(L.Row);
    }

    private static async Task RefusedAsync(RequestCqrsHostedCluster cluster, RequestCqrsHostedRuntimeFixture node,
        PrincipalRecord principal, GrainRequestEnvelope envelope, Guid requestId, Guid commandId, CancellationToken token)
    {
        await Task.WhenAll(cluster.Nodes.Select(actual => actual.Database.Coordinator.ReadBarrierAsync(token)));
        var before = cluster.Nodes.Select(actual => new
        {
            Node = actual,
            Rows = CanonicalStoreImage.Bytes(actual.Database.Store),
            Identity = OrleansSerialization.Serialize(actual.Database.Store.Identity),
            Cut = actual.Database.ReadCanonicalCut()
        }).ToArray();
        var reply = await RequestCqrsCommandStream.InvokeAsync(node, envelope, principal,
            requestId, commandId, TerminalSequence, ErrorCode.PermissionDenied);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(reply.SafeDetail).IsEqualTo(GrainRoutingProtocol.AdministrationRequired);
        await Assert.That(reply.Result).IsNull();
        foreach (var image in before)
        {
            var database = image.Node.Database;
            await Assert.That(CanonicalStoreImage.Bytes(database.Store).SequenceEqual(image.Rows, StringComparer.Ordinal)).IsTrue();
            await Assert.That(OrleansSerialization.Serialize(database.Store.Identity).SequenceEqual(image.Identity)).IsTrue();
            await Assert.That(database.ReadCanonicalCut()).IsEqualTo(image.Cut);
            await Assert.That(database.Database.LastApplied).IsEqualTo(image.Cut.AppliedPosition);
            await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
                KeySpace.GlobalOutcome(principal.Id, commandId)))).IsNull();
        }
    }

    private static async Task HealthyAsync(RequestCqrsHostedCluster cluster, DropTableRequest request,
        Guid commandId, DropTableResult actual, CancellationToken token)
    {
        foreach (var node in cluster.Nodes)
        {
            await node.Database.Coordinator.ReadBarrierAsync(token);
            var stored = OutcomeStoreOracle.ReadStored(node.Database.Database, commandId, token);
            var result = stored.Result.Get<DropTableResult>();
            await Assert.That(stored.PreparedCommand.CommandId).IsEqualTo(commandId);
            await Assert.That(stored.PreparedCommand.UserId).IsEqualTo(Root);
            await Assert.That(stored.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.DropTable);
            await CommandObservedPolicyAssertions.EqualAsync(request,
                ((DropTableRequest)stored.PreparedCommand.Value.Input));
            await Assert.That(stored.CanonicalSequence).IsEqualTo(actual.Receipt.Token.Position);
            var expected = new DropTableResult(request.TenantId, request.DatabaseId, request.ResourceName,
                new(commandId, new(node.Database.Store.Identity.Incarnation, CatalogRoute.PartitionId,
                    stored.CanonicalSequence), [], DurabilityProfile.ProcessDurable, LocalPersistedCopies));
            await CommandObservedPolicyAssertions.EqualAsync(expected, result);
            await Assert.That(actual.Receipt.Token).IsEqualTo(expected.Receipt.Token);
            await Assert.That(node.Database.Store.Read(view => view.ReadOwnedValue(
                KeySpace.Resource(request.TenantId, request.DatabaseId, request.ResourceName)))).IsNull();
        }
        var source = cluster.Nodes[SourceIndex].Database;
        var receipt = source.Commit(new PutDocument(Collection, DocumentId, Json));
        await Assert.That(receipt.PersistedCopies).IsEqualTo(PersistedCopies);
        await CommandObservedPolicyAssertions.EqualAsync(new MutationReceipt[]
            { new("putDocument", Collection, DocumentId, FirstRevision) }, receipt.Mutations.ToArray());
        var healthy = source.Database.GetDocument(Root, new(source.Partition, Collection, DocumentId),
            cancellationToken: token);
        await Assert.That(healthy!.Json).IsEqualTo(Json);
        await Assert.That(healthy.Revision).IsEqualTo(FirstRevision);
        await Assert.That(healthy.Redacted).IsFalse();
    }
}
