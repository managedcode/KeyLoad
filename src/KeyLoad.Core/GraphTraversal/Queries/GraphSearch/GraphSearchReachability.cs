namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Metadata-only shortest-hop result retained by a same-cut graph search.</summary>
internal readonly record struct GraphSearchReachability(EntityRef Reference, int ShortestHops);
