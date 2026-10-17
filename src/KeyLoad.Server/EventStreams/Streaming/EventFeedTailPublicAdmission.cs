using ManagedCode.Communication.CQRS;

namespace KeyLoad.Server.Features.EventStreams;

/// <summary>Settles the original native producer before exposing an owning typed admission refusal.</summary>
internal static class EventFeedTailPublicAdmission
{
    internal static async IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> ReadAsync(
        IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> source,
        EventFeedTailJsonGrant grant,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var owner = new EventFeedTailAdmissionOwner(source, grant, cancellationToken);
        await using (owner.ConfigureAwait(false))
        {
            while (await owner.MoveNextAsync().ConfigureAwait(false))
            {
                yield return owner.Current;
            }
        }
        if (owner.Quota is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return grant.AdmitFailure();
        }
    }
}
