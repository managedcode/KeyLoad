using KeyLoad.Core;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueDispatchCasFixture : IDisposable
{
    internal const string Queue = "dispatch-cas-queue";
    internal const string MissingQueue = "dispatch-cas-missing";
    internal const int InitialVersion = 1;
    internal const int VersionStep = 1;
    private const int Attempts = 4;
    private const string PrivatePath = "/secret";
    private const string HeaderPath = "/token";
    private const string Classification = "dispatch-private";
    private const string SetupAndCleanupFailed = "Queue dispatch fixture setup and cleanup both failed.";
    private readonly ResourcePolicyUpdateFixture owner = new();

    internal ResourceDefinition Definition { get; }
    internal byte[] OriginalDocument { get; }
    internal byte[] OriginalCollection { get; }

    internal QueueDispatchCasFixture(long version = InitialVersion)
    {
        Definition = new(Queue, ResourceKind.WorkQueue, ResourcePolicyUpdateFixture.DomainId)
        {
            SchemaVersion = version,
            FieldPolicies = [new(PrivatePath, Classification)],
            HeaderPolicies = [new(HeaderPath, Classification)],
            QueuePolicy = new() { MaxAttempts = Attempts }
        };
        try
        {
            var seed = new ConfigureResourceRequest(ResourcePolicyUpdateFixture.TenantId,
                ResourcePolicyUpdateFixture.DatabaseId, Definition);
            owner.Apply(owner.Database.CreateNativeOperation(OperationKind.ConfigureResource,
                Guid.NewGuid(), ResourcePolicyUpdateFixture.RootId, owner.Database.EvaluationClock.GetUtcNow(),
                NativeSerialization.Serialize(seed, NativeValidationProfile.PublicInputElements))).Get<ResourceDefinition>();
            OriginalDocument = owner.DocumentBytes() ?? throw new InvalidOperationException();
            OriginalCollection = owner.ResourceBytes() ?? throw new InvalidOperationException();
        }
        catch (Exception primary)
        {
            try
            { owner.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(SetupAndCleanupFailed, primary, cleanup); }
            throw;
        }
    }

    internal ReplicatedOperation Command(Guid id, long expected, bool paused,
        string principal = ResourcePolicyUpdateFixture.RootId, string queue = Queue)
    {
        var request = new QueueDispatchControlRequest(ResourcePolicyUpdateFixture.TenantId,
            ResourcePolicyUpdateFixture.DatabaseId, queue, expected, paused);
        return owner.Database.CreateNativeOperation(OperationKind.SetQueueDispatch, id, principal,
            owner.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request, NativeValidationProfile.PublicInputElements));
    }

    internal OperationResult Apply(ReplicatedOperation operation) => owner.Apply(operation);
    internal ResourceDefinition Current() => owner.Store.Read(view => view.GetRecord<ResourceDefinition>(
        KeySpace.Resource(ResourcePolicyUpdateFixture.TenantId, ResourcePolicyUpdateFixture.DatabaseId, Queue)))
        ?? throw new InvalidOperationException();
    internal byte[]? Document() => owner.DocumentBytes();
    internal byte[]? Collection() => owner.ResourceBytes();
    internal byte[]? Outcome(Guid id) => owner.OutcomeBytes(id);
    internal void Reopen() => owner.Reopen();
    internal OperationResult PolicyPauseAttempt() => owner.Apply(owner.ConfigureResource(
        Current() with { Paused = true, SchemaVersion = Current().SchemaVersion + VersionStep }, Current().SchemaVersion));
    internal static KeyLoadException Failure(OperationResult result) => ResourcePolicyUpdateFixture.Failure(result);
    public void Dispose() => owner.Dispose();
}
