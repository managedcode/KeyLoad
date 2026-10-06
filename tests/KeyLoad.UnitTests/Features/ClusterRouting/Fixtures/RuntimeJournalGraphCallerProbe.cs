using KeyLoad.Orleans;
using ManagedCode.Orleans.Graph.Extensions;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[GrainType(RuntimeJournalGraphCallerProtocol.ProbeAlias)]
internal sealed class RuntimeJournalGraphCallerProbeGrain(
    IGrainFactory grains,
    GrainRequestCodec codec,
    RuntimeJournalClient journalClient,
    IOptions<NativeRuntimeTestOptions> timingOptions) : Grain, IRuntimeJournalGraphCallerProbeGrain
{
    public async Task<RuntimeJournalGraphCallerResult> ExerciseAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(timingOptions.Value.CompletionTimeout, journalClient.Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var operationToken = linked.Token;
        var originalCaller = RequestContextHelper.CaptureCurrentCaller();
        var providerReadCompleted = false;
        var providerRestored = false;
        var wrongMethodDenied = false;
        var wrongMethodRestored = false;
        var wrongTargetDenied = false;
        var wrongTargetRestored = false;
        await RequestContextHelper.RunWithCurrentCallerAsync(typeof(RuntimeJournalGraphCallerProbeGrain).FullName!,
            nameof(ExerciseAsync), async () =>
            {
                var probeCaller = RequestContextHelper.CaptureCurrentCaller();
                operationToken.ThrowIfCancellationRequested();
                await journalClient.GetCatalogAsync(operationToken).ConfigureAwait(true);
                providerReadCompleted = true;
                providerRestored = Equals(probeCaller, RequestContextHelper.CaptureCurrentCaller());
                (wrongMethodDenied, wrongMethodRestored) = await DenyWrongMethodAsync(operationToken)
                    .ConfigureAwait(true);
                (wrongTargetDenied, wrongTargetRestored) = await DenyWrongTargetAsync(operationToken)
                    .ConfigureAwait(true);
            }).ConfigureAwait(true);

        return new(providerReadCompleted, providerRestored, wrongMethodDenied, wrongMethodRestored,
            wrongTargetDenied, wrongTargetRestored,
            Equals(originalCaller, RequestContextHelper.CaptureCurrentCaller()));
    }

    private async Task<(bool Denied, bool Restored)> DenyWrongMethodAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var signed = CreateCatalogRequest(requestId);
        var before = RequestContextHelper.CaptureCurrentCaller();
        var denied = false;
        await RequestContextHelper.RunWithCurrentCallerAsync(RuntimeJournalClient.CallerIdentity,
            nameof(RuntimeJournalClient.GetCatalogAsync), async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ReadRequestAsync(requestId, signed, cancellationToken).ConfigureAwait(true);
                }
                catch (InvalidOperationException failure)
                {
                    RuntimeJournalGraphCallerSupport.RequireGraphDenial(failure,
                        RuntimeJournalClient.CallerIdentity, typeof(IRequestGrain).FullName!);
                    denied = true;
                }
            }).ConfigureAwait(true);
        return (denied, Equals(before, RequestContextHelper.CaptureCurrentCaller()));
    }

    private async Task<(bool Denied, bool Restored)> DenyWrongTargetAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var signed = CreateCatalogRequest(requestId);
        var before = RequestContextHelper.CaptureCurrentCaller();
        var denied = false;
        await RequestContextHelper.RunWithCurrentCallerAsync(RuntimeJournalClient.CallerIdentity,
            RuntimeJournalClient.ReadCoreCallerMethod, async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await grains.GetGrain<IDatabaseReadGrain>(requestId)
                        .ExecuteAsync(signed, cancellationToken).ConfigureAwait(true);
                }
                catch (InvalidOperationException failure)
                {
                    RuntimeJournalGraphCallerSupport.RequireGraphDenial(failure,
                        RuntimeJournalClient.CallerIdentity, typeof(IDatabaseReadGrain).FullName!);
                    denied = true;
                }
            }).ConfigureAwait(true);
        return (denied, Equals(before, RequestContextHelper.CaptureCurrentCaller()));
    }

    private string CreateCatalogRequest(Guid requestId)
        => codec.CreateRuntimeJournalRead(requestId, GrainReadKind.RuntimeJournalCatalog,
            NativeSerialization.Serialize(GrainNativeContracts.NoDtoMarker));

    private async Task ReadRequestAsync(Guid requestId, string signed, CancellationToken cancellationToken)
    {
        await foreach (var chunk in grains.GetGrain<IRequestGrain>(requestId)
                           .ExecuteStreamAsync(signed, cancellationToken).ConfigureAwait(true))
        {
            _ = chunk;
        }
    }
}
