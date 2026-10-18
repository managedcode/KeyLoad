using System.Collections.Immutable;

namespace KeyLoad.Core;

/// <summary>Owns the bounded native keys of one disposable discovery prefix.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(DuePrefixCursor.SerializerAlias)]
public sealed record DuePrefixCursor([property: Orleans.Id(0)] bool HasUpperBound,
    [property: Orleans.Id(1)] ImmutableArray<byte>? UpperKey,
    [property: Orleans.Id(2)] ImmutableArray<byte>? LastKey)
{
    internal const string SerializerAlias = "keyload.core.due-prefix-cursor.v1";

    internal static DuePrefixCursor Empty { get; } = new(false, null, null);
}
