namespace KeyLoad.Comparisons;

/// <summary>An event returned by a comparison target.</summary>
/// <param name="EventId">The identifier assigned to the event.</param>
/// <param name="Revision">The event revision reported by the target.</param>
/// <param name="Json">The returned serialized event payload.</param>
public sealed record FoundEvent(Guid EventId, ulong Revision, string Json);
