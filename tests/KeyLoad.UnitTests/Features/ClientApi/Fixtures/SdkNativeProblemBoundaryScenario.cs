using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.Features.ClientApi.SdkNativeProblemBoundaryProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkNativeProblemBoundaryScenario(ConnectionNativeScenario native, SdkNativeProblemBoundaryTasks tasks)
{
    internal ConnectionNativeScenario Native => native;
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
    internal EntityRef Forbidden => native.Reference(P.Document);
    internal EntityRef Allowed => new(native.Fixture.Database.Partition, P.Allowed, P.Document);
    internal static Problem ExpectedProblem => Errors.Problem(ErrorCode.PermissionDenied, P.Denied);
    internal static int ProblemBytes => JsonDefaults.Serialize(ExpectedProblem).Length;

    internal async Task InitializeAsync(CancellationToken token)
    {
        await native.InitializeAsync(token);
        var db = native.Fixture.Database;
        db.Configure(P.Allowed, ResourceKind.Collection, db.Partition.TransactionDomainId);
        var record = new PrincipalRecord(P.Reader, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, P.Allowed, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            PolicyEpoch = P.InitialPolicy
        };
        Principal = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record)).Get<PrincipalRecord>();
        await SdkNativeProblemBoundaryAssertions.EqualAsync(record, Principal);
        await Assert.That(db.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(
            DatabaseEngine.Credential(P.KeyId, Principal.Id, P.Secret))).Get<bool>()).IsTrue();
        Seed = new(Guid.NewGuid(), db.Partition,
            [new PutDocument(Forbidden.Collection, P.Document, P.SeedJson, P.InitialRevision),
             new PutDocument(P.Allowed, P.Document, P.SeedJson, P.InitialRevision)]);
        SeedReceipt = db.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>();
        Denied = native.Command(P.Document, P.AttackJson, P.FirstRevision);
        Healthy = new(Guid.NewGuid(), db.Partition,
            [new PutDocument(P.Allowed, P.Document, P.HealthyJson, P.FirstRevision)]);
        await SdkNativeProblemBoundaryAssertions.ReceiptAsync(this, Seed, SeedReceipt, root: true);
        var refusal = await CommandAsync(Denied, token);
        await SdkNativeProblemBoundaryAssertions.RefusalAsync(refusal);
        await SdkNativeProblemBoundaryAssertions.FailedOutcomeAsync(this);
        await SdkNativeProblemBoundaryAssertions.CurrentAsync(this);
    }

    internal async Task<GrainOperationReply> CommandAsync(CommandRequest command, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var original = tasks.Retain(native.CommandAsync(Principal, requestId, command, token));
        var reply = await tasks.CompleteAsync(original);
        await native.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        return reply;
    }

    internal async Task<GrainOperationReply> ReplaySeedAsync(CancellationToken token)
    {
        var db = native.Fixture.Database;
        var root = db.Store.Read(view => db.Database.Principal(view, P.Root, native.Fixture.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var original = tasks.Retain(native.CommandAsync(root, requestId, Seed, token));
        var reply = await tasks.CompleteAsync(original);
        await native.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        return reply;
    }

    internal async Task<GrainOperationReply> ReadAsync(EntityRef reference, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var signed = native.Fixture.Codec.CreateRead(requestId, Principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(reference)));
        var original = tasks.Retain(native.InvokeAsync(Principal, requestId, Guid.Empty, signed, token));
        var reply = await tasks.CompleteAsync(original);
        await native.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        return reply;
    }
}
