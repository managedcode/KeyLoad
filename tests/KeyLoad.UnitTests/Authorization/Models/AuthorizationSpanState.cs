namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record AuthorizationSpanState(string Source, bool Recorded, string TraceId,
    string SpanId, string ParentSpanId, string[] Events, AuthorizationLinkState[] Links,
    int LocalParentCount, string[] ParentOperations, KeyValuePair<string, string?>[] Baggage,
    KeyValuePair<string, string?>[] Tags, int MaximumParents, int MaximumBaggage, int MaximumTags);
