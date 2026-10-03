namespace KeyLoad.Client;

/// <summary>Transforms one event payload from a schema to its immediately following version.</summary>
/// <param name="FromVersion">The positive source schema version.</param>
/// <param name="ToVersion">The exact source version plus one.</param>
/// <param name="Transform">A caller-owned transform that may change payload JSON only.</param>
public sealed record EventUpcaster(int FromVersion, int ToVersion, Func<EventData, EventData> Transform);
