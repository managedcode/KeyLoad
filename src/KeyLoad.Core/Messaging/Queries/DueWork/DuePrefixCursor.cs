namespace KeyLoad.Core;

internal sealed record DuePrefixCursor(bool HasUpperBound, byte[]? UpperKey, byte[]? LastKey)
{
    internal static DuePrefixCursor Empty { get; } = new(false, null, null);
}
