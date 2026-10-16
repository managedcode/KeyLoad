namespace KeyLoad.Core;

/// <summary>Retains the original owned follower snapshot and its same-cut D1 for this in-process operation only.</summary>
internal sealed record FollowerDocumentLifetimeSnapshot(FollowerDocumentSnapshot Original, ResourceLifetimeV1 Lifetime);
