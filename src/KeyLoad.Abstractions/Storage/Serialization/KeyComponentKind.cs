namespace KeyLoad.Storage;

/// <summary>The typed component shapes a version 1 key can carry.</summary>
internal enum KeyComponentKind : byte
{
    Null,
    Missing,
    Boolean,
    Int64,
    Decimal,
    Double,
    Timestamp,
    Text,
    Binary
}
