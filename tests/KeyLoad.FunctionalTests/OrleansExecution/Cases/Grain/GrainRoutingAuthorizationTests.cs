using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.OrleansExecution;

namespace KeyLoad.FunctionalTests;

/// <summary>AC-ROUTE-001/AUTH-001: real persisted authority, canonical partition routing and durable retry outcomes.</summary>
internal sealed class GrainRoutingAuthorizationTests
{
    private const string Root = "root";
    private const string MissingPrincipal = "missing-principal";
    private const string PublicPrincipal = "reader";
    private const string Credential = "root.unit-test-credential-32-characters";
    private const string Collection = "orders";
    private const string DocumentId = "order-1";
    private const string DocumentJson = "{\"value\":42}";
    private const string OtherDomain = "another-domain";
    private const int NoDtoMarker = 0;
    private const int InvalidMarker = 1;
    private const int FirstRevision = 1;
    private const long BackupPosition = 42;
    private const long PolicyEpochIncrement = 1;

    /// <summary>AC-ROUTE-001: API-key authentication returns the current persisted principal, without trusting caller roles.</summary>
    [Test]
    public async Task AuthenticationReloadsPersistedPrincipalAndRevocationIsImmediate()
    {
        using var fixture = new TestDatabase();
        var principal = GrainRequestAuthority.Authenticate(fixture.Database, Credential, TimeProvider.System);
        await Assert.That(principal.Id).IsEqualTo(Root);
        await Assert.That(principal.ClusterAdministrator).IsTrue();
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            Revoked = true,
            PolicyEpoch = principal.PolicyEpoch + PolicyEpochIncrement
        }));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainRequestAuthority.Authenticate(fixture.Database,
            Credential, TimeProvider.System)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-AUTH-001: an issued principal ID still requires a current persisted row and administrator grant.</summary>
    [Test]
    public async Task MissingPrincipalAndUnprivilegedAdministrationFailClosed()
    {
        using var fixture = new TestDatabase();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainRequestAuthority.Reload(fixture.Database,
            MissingPrincipal, TimeProvider.System)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        var principal = new PrincipalRecord(PublicPrincipal, fixture.Partition.TenantId, [], []);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal));
        var persisted = GrainRequestAuthority.Reload(fixture.Database, PublicPrincipal, TimeProvider.System);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainRequestAuthority.RequireAdministrator(persisted)).Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    /// <summary>AC-ROUTE-001: stable command IDs preserve durable effects while request IDs remain independent.</summary>
    [Test]
    public async Task FreshRequestActorsKeepOneStableWriteOutcomeAndRejectWrongPartition()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(Collection, ResourceKind.Collection);
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System);
        var executor = new GrainCommandExecutor(fixture.Database, new EmbeddedCoordinator(fixture.Database), TimeProvider.System);
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, fixture.Partition, [new PutDocument(Collection, DocumentId, DocumentJson)]);
        var first = codec.Decode(codec.CreateCommand(Guid.NewGuid(), Root, OperationKind.Batch, commandId, command));
        var second = codec.Decode(codec.CreateCommand(Guid.NewGuid(), Root, OperationKind.Batch, commandId, command));
        var key = GrainPartitionResolver.Resolve(first);
        await Assert.That(first.Envelope.RequestId).IsNotEqualTo(second.Envelope.RequestId);
        var initial = await SignedGrainRequestTestContext.ExecuteAsync(executor, first, key, CancellationToken.None);
        var replay = await SignedGrainRequestTestContext.ExecuteAsync(executor, second, key, CancellationToken.None);
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(initial)).AsSpan().SequenceEqual(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(replay)))).IsTrue();
        await Assert.That(fixture.Database.GetDocument(Root, new(fixture.Partition, Collection, DocumentId))!.Revision).IsEqualTo(FirstRevision);
        var wrongKey = (fixture.Partition with
        {
            TransactionDomainId = OtherDomain
        }).AtomicPartitionId;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => SignedGrainRequestTestContext.ExecuteAsync(executor, second, wrongKey, CancellationToken.None))
            ?? throw new InvalidOperationException();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: a previously issued request cannot retain authority after persisted revocation.</summary>
    [Test]
    public async Task IssuedWriteReloadsPersistedRevocationBeforeCommit()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(Collection, ResourceKind.Collection);
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System);
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, fixture.Partition, [new PutDocument(Collection, DocumentId, DocumentJson)]);
        var request = codec.Decode(codec.CreateCommand(Guid.NewGuid(), Root, OperationKind.Batch, commandId, command));
        var principal = GrainRequestAuthority.Reload(fixture.Database, Root, TimeProvider.System);
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        {
            Revoked = true,
            PolicyEpoch = principal.PolicyEpoch + PolicyEpochIncrement
        }));
        var executor = new GrainCommandExecutor(fixture.Database, new EmbeddedCoordinator(fixture.Database), TimeProvider.System);
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => SignedGrainRequestTestContext.ExecuteAsync(executor, request,
            fixture.Partition.AtomicPartitionId, CancellationToken.None))
            ?? throw new InvalidOperationException();
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, Root, commandId)).IsNull();
    }

    /// <summary>AC-ROUTE-001: queue dispatch control is a catalog command, so it routes to the catalog partition.</summary>
    [Test]
    public async Task QueueDispatchControlRoutesToTheCatalogPartition()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System);
        var control = new QueueDispatchControlRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, Collection,
            FirstRevision, true);
        var request = codec.Decode(codec.CreateCommand(Guid.NewGuid(), Root, OperationKind.SetQueueDispatch, Guid.NewGuid(),
            control));

        await Assert.That(GrainPartitionResolver.Resolve(request)).IsEqualTo(GrainRoutingProtocol.CatalogPartition);
    }

    /// <summary>AC-ROUTE-001: envelope and embedded command IDs cannot disagree, even with a genuine signature.</summary>
    [Test]
    public async Task EmbeddedWriteIdentityAndCancellationAreCheckedBeforeEffects()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, []);
        var request = codec.Decode(codec.CreateCommand(Guid.NewGuid(), Root, OperationKind.Batch, Guid.NewGuid(), command));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPartitionResolver.Resolve(request)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var valid = codec.Decode(codec.CreateCommand(Guid.NewGuid(), Root, OperationKind.Batch, command.CommandId, command));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var executor = new GrainCommandExecutor(fixture.Database, new EmbeddedCoordinator(fixture.Database), TimeProvider.System);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => SignedGrainRequestTestContext.ExecuteAsync(executor, valid,
            fixture.Partition.AtomicPartitionId, cancellation.Token));
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Store, fixture.Partition, Root, command.CommandId)).IsNull();
    }

    /// <summary>AC-ROUTE-001: no-DTO requests use the exact execution zero marker and replies preserve typed values.</summary>
    [Test]
    public async Task ExactMarkerAndBoundedRepliesPreserveTypedValues()
    {
        GrainPayload.RequireNoDto(NoDtoMarker);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPayload.RequireNoDto(
            InvalidMarker)).Code).IsEqualTo(ErrorCode.Validation);
        var reply = GrainReplyFactory.Value(new BackupReceipt(DocumentId, BackupPosition));
        await Assert.That(((BackupReceipt)GrainReplyFactory.RequireValue(reply).Value!).Id).IsEqualTo(DocumentId);
        await Assert.That(reply.Error).IsNull();
        var error = GrainReplyFactory.Failure(Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired), false, null);
        await Assert.That(error.Result).IsNull();
        await Assert.That(error.Error).IsEqualTo(ErrorCode.PermissionDenied);
    }
}
