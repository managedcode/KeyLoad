namespace KeyLoad;

/// <summary>Names only canonical authorized hybrid ranking branches.</summary>
public enum SearchBranchKind
{
    /// <summary>The canonical lexical branch.</summary>
    Text,
    /// <summary>The canonical vector similarity branch.</summary>
    Vector,
    /// <summary>The canonical shortest-hop graph retrieval branch.</summary>
    Graph
}
