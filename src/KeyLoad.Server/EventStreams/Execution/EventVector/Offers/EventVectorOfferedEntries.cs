using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorOfferedEntries
{
    internal static ImmutableArray<EventVectorEntry> Create(ImmutableArray<EventVectorEntry> acknowledged,
        ImmutableArray<EventSourcePage> actualPages, EventVectorAdmissionPolicy admission)
        => EventVectorOfferPageProjection.Create(acknowledged, actualPages, admission);
}
