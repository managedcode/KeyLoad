namespace KeyLoad;

/// <summary>Selects the distance function used to compare vectors.</summary>
public enum DistanceMetric
{
    /// <summary>Compare vectors using cosine distance.</summary>
    Cosine,
    /// <summary>Compare vectors using Euclidean distance.</summary>
    Euclidean,
    /// <summary>Compare vectors using dot product.</summary>
    DotProduct
}
