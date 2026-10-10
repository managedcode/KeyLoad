using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

internal static class ProtectedDiscoveryHandlerCreation
{
    internal static HttpMessageHandler Create(PeerSecurity security,
        Action<HttpRequestMessage, CancellationToken> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var failures = new List<Exception>();
        var pending = security.CreateHandler();
        try
        {
            var signed = (DelegatingHandler)pending;
            var original = signed.InnerHandler ?? throw new InvalidOperationException(InvalidOwner);
            signed.InnerHandler = new ObservationHandler(observed) { InnerHandler = original };
            return pending;
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        try
        { pending.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        { failures.Add(error); }
        ThrowIfAny(failures);
        throw new InvalidOperationException(InvalidOwner);
    }

    private const string InvalidOwner = "The protected discovery handler owner is invalid.";
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;
    private static void ThrowIfAny(List<Exception> failures)
    {
        if (failures.Count == SingleFailure)
        { ExceptionDispatchInfo.Capture(failures[FirstFailure]).Throw(); }
        if (failures.Count > SingleFailure)
        { throw new AggregateException(failures); }
    }

    private sealed class ObservationHandler(Action<HttpRequestMessage, CancellationToken> observed) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            { observed(request, cancellationToken); }
            catch (Exception original) when (original is IOException or HttpRequestException)
            { throw new AggregateException(original); }
            cancellationToken.ThrowIfCancellationRequested();
            return base.SendAsync(request, cancellationToken);
        }
    }
}
