namespace KeyLoad.Storage;

/// <summary>The typed component shapes a version 1 key can carry.</summary>
public enum KeyComponentKind
{
    /// <summary>An explicit null.</summary>
    Null,
    /// <summary>An absent value, ordered before null.</summary>
    Missing,
    /// <summary>A boolean.</summary>
    Boolean,
    /// <summary>A signed 64-bit integer.</summary>
    WholeNumber,
    /// <summary>An exact decimal number.</summary>
    FixedPoint,
    /// <summary>A finite double.</summary>
    FloatingPoint,
    /// <summary>A UTC timestamp.</summary>
    Timestamp,
    /// <summary>Ordinal UTF-8 text.</summary>
    Text,
    /// <summary>An owned byte sequence.</summary>
    Binary
}
