using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns context transport responsibility while borrowing the original HTTP, native address pins and configured authority.</summary>
internal sealed class PartitionMovementTransportContext : IDisposable
{
    internal const string OriginalFailureKey = "KeyLoad.PartitionMovement.OriginalTransportFailure";
    private readonly NodeOptions options;
    private readonly PartitionMovementTransportResources resources;
    internal PartitionHost Partition { get; }
    internal IOptions<OrleansMembershipOptions> Membership { get; }
    internal TimeProvider Clock { get; }
    internal NodeOptions Options => options;
    internal PartitionMovementTransportResources Resources => resources;

    internal PartitionMovementTransportContext(IOptions<NodeOptions> options, PartitionHost partition,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        this.options = options.Value;
        Partition = partition;
        Membership = membership;
        Clock = clock;
        resources = new(options, partition, membership);
    }
    internal async Task<HttpResponseMessage> SendOutcomeRequestAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException failure) when (failure.StatusCode is null)
        { throw new PartitionMovementOutcomeNetworkException(failure); }
    }

    public void Dispose() => resources.Dispose();

    internal static bool IsUnresolvedTransport(Exception error)
        => error is HttpRequestException or IOException or TimeoutException
            || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(IsUnresolvedTransport);
}
