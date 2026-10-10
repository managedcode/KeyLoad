using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferOriginalNonceTrial : IAsyncDisposable
{
    private const string SubjectArgument = "--KeyLoad:RemoteTransferPrincipalId=";
    private readonly List<Exception> failures;
    private readonly PartitionMovementLateNativeOwners owners;
    private readonly RemoteTransferOriginalRequestGate response;
    private readonly Guid acceptId = Guid.NewGuid();
    private PartitionMovementBlobWireCallers? administrator;
    private RemoteTransferPostAwaitCallers? calls;
    private Task? producer;
    private Task? disposal;
    private Task<KeyLoadException>? endpointSettlement;

    private RemoteTransferOriginalNonceTrial(List<Exception> failures, CancellationToken caller)
    {
        this.failures = failures;
        response = new(acceptId, caller);
        owners = new(configureArguments: ConfigureArguments, configureApplication: (index, app) =>
        { if (index >= PartitionMovementLateNativeSettings.GroupSize) { response.Attach(index, app); } });
    }

    private static void ConfigureArguments(int index, List<string> arguments)
    {
        if (index >= PartitionMovementLateNativeSettings.GroupSize)
        { arguments.Add(SubjectArgument + RemoteTransferDistinctProtocol.TechnicalSubject); }
    }

    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        await using (var trial = new RemoteTransferOriginalNonceTrial(failures, lifetime.Token))
        {
            try
            {
                trial.producer = trial.ExecuteAsync(lifetime.Token);
                await ServerFailureObserver.ObserveAsync(() => trial.producer, failures);
            }
            finally { ServerFailureObserver.Observe(lifetime.Cancel, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        await owners.StartAsync(token);
        administrator = new(owners.Settings);
        await administrator.InitializeAsync(owners.Settings, token);
        var parent = new PartitionMovementBlobWireSeed(administrator);
        await parent.CreateAsync(owners.Settings, token);
        var setup = await RemoteTransferPostAwaitSetup.CreateAsync(administrator, parent, token);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await administrator.Source.MovePartitionAsync(parent.Request, token));
        await Assert.That(terminal.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await parent.RequireAsync(token);
        calls = new(new(owners.Settings.Origin(0)), setup.Seed.Identity.Secret);
        await calls.InitializeAsync(setup.Seed.Identity.Secret, token);
        var created = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(setup.Seed.Create, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(created, setup.Seed.Create,
            setup.Seed.Scenario.SourceQueue, setup.Seed.TransferId,
            RemoteTransferColdProtocol.CreateKind, RemoteTransferColdProtocol.CreateRevision);
        await calls.ReplayAsync(setup.Seed.Create, created, token);
        var intent = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.InspectQueueTransferAsync(setup.Seed.SourceRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        await Assert.That(intent.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(intent.ReceiptToken).IsNull();
        await RemoteTransferPostAwaitAssertions.StateAsync(calls, setup.Seed, intent, null, null, token);
        var accept = setup.Seed.Accept(intent) with { CommandId = acceptId };
        await ExecuteWireAsync(setup.Seed, accept, token);
        await calls.DisposeAsync();
        calls = null;
        await RemoteTransferNativeWireContinuation.ExecuteAsync(owners, setup.Seed, intent, accept, created, token);
        await administrator.RequireAsync(parent.Partition, McpToolNames.AdminPartitionMove, parent.Request,
            ct => administrator.Source.MovePartitionAsync(parent.Request, ct), terminal, token);
        await parent.RequireAsync(token);
    }

    private async Task ExecuteWireAsync(RemoteTransferColdSeed seed, CommandRequest accept, CancellationToken token)
    {
        var current = calls ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        var original = current.Sdk.CommitAsync(accept, token);
        owners.TrackProducer(original);
        try
        {
            var first = await Task.WhenAny(response.Held, original).WaitAsync(token);
            if (first == original)
            { _ = await original; throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
            var held = await response.Held;
            var settled = ObserveEndpoint();
            owners.TrackProducer(settled);
            var native = await RemoteTransferOriginalNonceSend.ExecuteAsync(owners, seed, response, held, accept, token);
            response.Release();
            await QueueProducerRf3Assertions.DeniedAsync(await original, ErrorCode.UnknownWriteOutcome);
            var rejected = await settled;
            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Unauthenticated);
            await native.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
            var before = RemoteTransferWireNativeCut.Read(owners, seed, token);
            await current.ReplayAsync(accept, native.OriginalReceipt, token);
            await before.RequireAsync(RemoteTransferWireNativeCut.Read(owners, seed, token));
        }
        finally
        {
            response.Release();
            if (response.OriginalProducer is not null)
            { owners.TrackProducer(ObserveEndpoint()); }
        }
    }

    private Task<KeyLoadException> ObserveEndpoint()
        => endpointSettlement ??= RemoteTransferOriginalNonceSettlement.ReadAsync(response.OriginalProducer
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing));

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        var cleanup = new List<Exception>();
        response.Release();
        if (producer is { } original)
        { owners.TrackProducer(original); }
        if (response.OriginalProducer is not null)
        { owners.TrackProducer(ObserveEndpoint()); }
        await ServerFailureObserver.ObserveAsync(owners.StopAsync, cleanup);
        if (producer is { } tracked)
        { await ServerFailureObserver.ObserveAsync(() => owners.JoinProducerAsync(tracked), cleanup); }
        if (response.OriginalProducer is not null)
        { await ServerFailureObserver.ObserveAsync(() => owners.JoinProducerAsync(ObserveEndpoint()), cleanup); }
        if (producer is { IsCompleted: false } || response.OriginalProducer is { IsCompleted: false })
        { owners.RetainRoots(); cleanup.Add(new InvalidOperationException(RemoteTransferDistinctProtocol.Missing)); }
        else
        {
            if (calls is not null)
            {
                try
                { await calls.DisposeAsync(); }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
            }
            if (administrator is not null)
            {
                try
                { await administrator.DisposeAsync(); }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
            }
        }
        try
        { await owners.DisposeAsync(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
        failures.AddRange(cleanup);
    }
}
