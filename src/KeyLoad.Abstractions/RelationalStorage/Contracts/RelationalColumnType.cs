namespace KeyLoad;

/// <summary>Exact scalar types supported by the typed relational row schema.</summary>
public enum RelationalColumnType
{
    /// <summary>A JSON string.</summary>
    Text,
    /// <summary>A JSON boolean.</summary>
    Boolean,
    /// <summary>An exact signed 64-bit JSON integer.</summary>
    WholeNumber,
    /// <summary>A JSON number representable as a CLR decimal.</summary>
    FixedPoint,
    /// <summary>An ISO timestamp with an explicit UTC offset.</summary>
    UtcTimestamp
}
