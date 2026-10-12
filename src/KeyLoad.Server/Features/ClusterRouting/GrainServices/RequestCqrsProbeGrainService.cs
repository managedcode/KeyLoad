using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Fixture-enabled native endpoint for original producer disposal facts; never a database execution endpoint.</summary>
internal sealed class RequestCqrsProbeGrainService(GrainId id, Silo silo, ILoggerFactory loggerFactory,
    RequestCqrsProbeNativeRelay relay, RequestCqrsProbeObserver observer)
    : GrainService(id, silo, loggerFactory), IRequestCqrsProbeGrainService
{
    private const int FailureEventId = 1;
    private static readonly Action<ILogger, Exception?> LogFailure = LoggerMessage.Define(LogLevel.Error,
        new EventId(FailureEventId), RequestCqrsProbeNativeProtocol.Unavailable);
    private readonly ILogger diagnostics = loggerFactory.CreateLogger<RequestCqrsProbeGrainService>();

    public override async Task Init(IServiceProvider serviceProvider)
    {
        await base.Init(serviceProvider).ConfigureAwait(true);
        observer.AttachProducerDisposalRelay(relay);
    }

    public Task<RequestCqrsProbeNativeReply> LocateProducerClaimAsync(RequestCqrsProbeNativeRequest request,
        CancellationToken cancellationToken)
        => ExchangeAsync(RequestCqrsProbeNativeMethod.Locate, request, cancellationToken);

    public Task<RequestCqrsProbeNativeReply> NotifyProducerDisposedAsync(RequestCqrsProbeNativeRequest request,
        CancellationToken cancellationToken)
        => ExchangeAsync(RequestCqrsProbeNativeMethod.Notify, request, cancellationToken);

    private async Task<RequestCqrsProbeNativeReply> ExchangeAsync(RequestCqrsProbeNativeMethod method,
        RequestCqrsProbeNativeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await relay.ExecuteAsync(method, request, observer.ProducerDisposal,
                error => LogFailure(diagnostics, error), cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            LogFailure(diagnostics, error);
            // An unadmitted request cannot obtain an authenticated error receipt.
            return new(RequestCqrsProbeNativeProtocol.Version, method, request?.Nonce ?? Guid.Empty,
                null, false, ErrorCode.Unauthenticated, string.Empty);
        }
    }

    public override async Task Stop()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(relay.StopAsync, failures).ConfigureAwait(true);
        await ServerFailureObserver.ObserveAsync(base.Stop, failures).ConfigureAwait(true);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
