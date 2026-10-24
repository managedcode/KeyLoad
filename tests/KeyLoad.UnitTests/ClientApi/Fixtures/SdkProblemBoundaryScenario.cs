using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.UnitTests.OrleansExecution;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.ClientApi.SdkProblemBoundaryProtocol;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class SdkProblemBoundaryScenario(ConnectionScenario execution, SdkProblemBoundaryTasks tasks)
{
    internal ConnectionScenario Execution => execution;
    internal PrincipalRecord Principal
    {
        get;
        private set;
    } = null!;
    internal CommandRequest Seed
    {
        get;
        private set;
    } = null!;
    internal CommitReceipt SeedReceipt
    {
        get;
        private set;
    } = null!;
    internal CommitToken? SeedToken { get; private set; }
    internal CommitToken? HealthyToken { get; set; }
    internal CommandRequest Denied
    {
        get;
        private set;
    } = null!;
    internal CommandRequest Healthy
    {
        get;
        private set;
    } = null!;
    internal CommitReceipt? HealthyReceipt
    {
        get;
        set;
    }
    internal EntityRef Forbidden => execution.Reference(P.Document);
    internal EntityRef Allowed
    {
        get;
        private set;
    } = null!;
    internal static Problem ExpectedProblem => Errors.Problem(ErrorCode.PermissionDenied, P.Denied);
    internal static int ProblemBytes => JsonDefaults.Serialize(ExpectedProblem).Length;

    internal async Task InitializeAsync(CancellationToken token)
    {
        await execution.InitializeAsync(token);
        var db = execution.Fixture.Database;
        await ConfigureAllowedAsync();
        var record = new PrincipalRecord(P.Reader, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, P.Allowed, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            PolicyEpoch = P.InitialPolicy
        };
        Principal = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record)).Get<PrincipalRecord>();
        await SdkProblemBoundaryAssertions.EqualAsync(record, Principal);
        await Assert.That(db.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(
            AuthorizationExecution.Credential(P.KeyId, Principal.Id, P.Secret))).Get<bool>()).IsTrue();
        Seed = new(Guid.NewGuid(), db.Partition,
            [new PutDocument(Forbidden.Collection, P.Document, P.SeedJson, P.InitialRevision),
             new PutDocument(P.Allowed, P.Document, P.SeedJson, P.InitialRevision)]);
        SeedToken = ExpectedCanonicalCommit.Next(db.Database, Seed.Partition);
        SeedReceipt = db.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>();
        Denied = execution.Command(P.Document, P.AttackJson, P.FirstRevision);
        Healthy = new(Guid.NewGuid(), db.Partition,
            [new PutDocument(P.Allowed, P.Document, P.HealthyJson, P.FirstRevision)]);
        await SdkProblemBoundaryAssertions.ReceiptAsync(this, Seed, SeedReceipt, root: true);
        var beforeRefusal = SdkProblemBoundaryAssertions.Capture(this);
        var refusal = await CommandAsync(Denied, token);
        await SdkProblemBoundaryAssertions.RefusalAsync(refusal);
        await SdkProblemBoundaryAssertions.UncommittedRefusalAsync(this);
        await SdkProblemBoundaryAssertions.SameAsync(this, beforeRefusal);
    }

    private async Task ConfigureAllowedAsync()
    {
        var db = execution.Fixture.Database;
        var definition = new ResourceDefinition(P.Allowed, ResourceKind.Collection, db.Partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition);
        var commandId = Guid.NewGuid();
        var configured = db.Submit(OperationKind.ConfigureResource, request, id: commandId).Get<ResourceDefinition>();
        await SdkProblemBoundaryAssertions.EqualAsync(definition, configured);
        var original = db.ReadStoredOutcome(commandId).PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(original.CommandId).IsEqualTo(commandId);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(original.UserId!).IsEqualTo(P.Root);
        await SdkProblemBoundaryAssertions.EqualAsync(request, OrleansSerialization.Deserialize<ConfigureResourceRequest>((original).Value!.Input.Span));
        var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, definition);
        Allowed = new(db.Partition, P.Allowed, P.Document)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
    }

    internal async Task<GrainOperationReply> CommandAsync(CommandRequest command, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var original = tasks.Retain(execution.CommandAsync(Principal, requestId, command, token));
        var reply = await tasks.CompleteAsync(original);
        var observed = execution.Observation.Read(requestId);
        await Assert.That(observed.Identity.RequestId).IsEqualTo(requestId);
        await Assert.That(observed.Identity.CommandId).IsEqualTo(command.CommandId);
        await observed.Disposed.Task.WaitAsync(token);
        return reply;
    }

    internal async Task<GrainOperationReply> ReplaySeedAsync(CancellationToken token)
    {
        var db = execution.Fixture.Database;
        var root = db.Store.Read(view => db.Database.Principal(view, P.Root, execution.Fixture.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var original = tasks.Retain(execution.CommandAsync(root, requestId, Seed, token));
        var reply = await tasks.CompleteAsync(original);
        await execution.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        return reply;
    }

    internal async Task<GrainOperationReply> ReadAsync(EntityRef reference, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var envelope = execution.Fixture.Codec.CreateRead(requestId, Principal.Id, GrainReadKind.Document,
            OrleansSerialization.Serialize(new GetDocumentRequest(reference)));
        var original = tasks.Retain(execution.InvokeAsync(Principal, requestId, Guid.Empty, envelope, token));
        var reply = await tasks.CompleteAsync(original);
        await execution.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        return reply;
    }
}
