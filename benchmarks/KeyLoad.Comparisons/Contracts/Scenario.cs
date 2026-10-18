namespace KeyLoad.Comparisons;

/// <summary>Identifies a workload operation measured by the comparison harness.</summary>
public enum Scenario
{
    /// <summary>Reads one previously initialized document by its identifier.</summary>
    PointRead,
    /// <summary>Writes a generated document.</summary>
    DocumentWrite,
    /// <summary>Finds the exact top matching vectors in the initialized corpus.</summary>
    VectorExact,
    /// <summary>Enqueues, receives, and acknowledges one queue message.</summary>
    QueueCycle,
    /// <summary>Reads the exact outgoing-neighbor documents for a graph vertex.</summary>
    GraphNeighbors,
    /// <summary>Traverses the generated graph from a starting vertex.</summary>
    GraphTraverse,
    /// <summary>Appends a generated event to a document stream.</summary>
    StreamAppend,
    /// <summary>Reads an event from a document stream.</summary>
    StreamRead,
    /// <summary>Replaces an existing document and verifies its exact changed body.</summary>
    DocumentUpdate,
    /// <summary>Deletes an existing document and verifies its absence.</summary>
    DocumentDelete
}
