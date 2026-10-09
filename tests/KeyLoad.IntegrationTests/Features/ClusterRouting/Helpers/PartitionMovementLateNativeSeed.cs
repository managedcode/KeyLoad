using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Real persisted resource and original public command, preserved across both cold continuations.</summary>
internal sealed class PartitionMovementLateNativeSeed : IDisposable
{
    private const int FirstOwner = 0;
    private const int CurrentProtocol = 1;
    private const long OriginalRevision = 1;
    private const long HealthyRevision = 2;
    private const string Collection = "late-native-documents";
    private const string DocumentId = "original";
    private const string OriginalJson = "{\"value\":1,\"flow\":\"native-late-retire\"}";
    private const string HealthyJson = "{\"value\":2,\"flow\":\"native-late-retire\"}";
    private readonly PartitionMovementLateNativeHttp sourceHttp;
    private readonly PartitionMovementLateNativeHttp targetHttp;
    internal KeyLoadClient Source { get; }
    internal KeyLoadClient Target { get; }
    internal PartitionRef Partition { get; } = new("tenant", "database", "late-native", PartitionMovementLateNativeProtocol.AtomicPartitionId);
    internal PartitionMoveRequest Request { get; private set; } = null!;
    private CommandRequest original = null!;
    private CommitReceipt receipt = null!;

    internal PartitionMovementLateNativeSeed(PartitionMovementLateNativeSettings settings)
    {
        PartitionMovementLateNativeHttp? source = null;
        PartitionMovementLateNativeHttp? target = null;
        KeyLoadClient? sourceClient = null;
        KeyLoadClient? targetClient = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            source = PartitionMovementLateNativeHttp.Create(new(settings.Origin(FirstOwner)));
            target = PartitionMovementLateNativeHttp.Create(new(settings.Origin(PartitionMovementLateNativeSettings.GroupSize)));
            sourceClient = new(source.Client, settings.AdministratorKey, IntegrationClientOptions.Execution());
            targetClient = new(target.Client, settings.AdministratorKey, IntegrationClientOptions.Execution());
        }, failures);
        if (failures.Count != 0)
        {
            if (target is not null)
            { ServerFailureObserver.Observe(() => target.Dispose(), failures); }
            if (source is not null)
            { ServerFailureObserver.Observe(() => source.Dispose(), failures); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        Source = sourceClient ?? throw new InvalidOperationException("The actual source SDK was not created.");
        Target = targetClient ?? throw new InvalidOperationException("The actual target SDK was not created.");
        sourceHttp = source ?? throw new InvalidOperationException("The actual source client was not created.");
        targetHttp = target ?? throw new InvalidOperationException("The actual target client was not created.");
    }

    internal async Task CreateAsync(PartitionMovementLateNativeSettings settings, CancellationToken cancellationToken)
    {
        _ = await McpCallerAssertions.SdkSuccessAsync(await Source.ConfigureResourceAsync(Guid.NewGuid(),
            new(Partition.TenantId, Partition.DatabaseId, new(Collection, ResourceKind.Collection, Partition.TransactionDomainId)), cancellationToken));
        var placement = await McpCallerAssertions.SdkSuccessAsync(await Source.ReadAtomicPartitionPlacementAsync(new(CurrentProtocol, Partition), cancellationToken));
        original = new(Guid.NewGuid(), Partition, [new PutDocument(Collection, DocumentId, OriginalJson)], placement.PlacementEpoch);
        receipt = await McpCallerAssertions.SdkSuccessAsync(await Source.CommitAsync(original, cancellationToken));
        Request = new(Guid.NewGuid(), Partition, settings.Destination, placement.Revision, PartitionMoveMode.Transfer);
        await RequireOriginalAsync(cancellationToken);
    }

    internal async Task RequireOriginalAsync(CancellationToken cancellationToken)
    {
        var document = await McpCallerAssertions.SdkSuccessAsync(await Source.GetAsync(new(Partition, Collection, DocumentId), cancellationToken));
        await Assert.That(document!.Json).IsEqualTo(OriginalJson);
        await Assert.That(document.Revision).IsEqualTo(OriginalRevision);
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await Source.CommitAsync(original, cancellationToken)));
    }

    internal async Task RequireHealthyAsync(PartitionMoveResult terminal, CancellationToken cancellationToken)
    {
        await Assert.That(terminal.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await RequireOriginalAsync(cancellationToken);
        var placement = terminal.PublishedPlacement ?? throw new InvalidOperationException("The actual terminal lacks placement.");
        var command = new CommandRequest(Guid.NewGuid(), Partition,
            [new PutDocument(Collection, DocumentId, HealthyJson, ExpectedRevision: OriginalRevision)], placement.PlacementEpoch);
        _ = await McpCallerAssertions.SdkSuccessAsync(await Target.CommitAsync(command, cancellationToken));
        var document = await McpCallerAssertions.SdkSuccessAsync(await Target.GetAsync(new(Partition, Collection, DocumentId), cancellationToken));
        await Assert.That(document!.Json).IsEqualTo(HealthyJson);
        await Assert.That(document.Revision).IsEqualTo(HealthyRevision);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        { targetHttp.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { sourceHttp.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
