using System.Runtime.CompilerServices;

namespace KeyLoad.Features.InternalSerialization;

internal readonly record struct NativeGraphKey(object Value, NativeValueValidation? Validation);

// Record payloads can have expensive structural equality. Memoization always compares the
// payload by identity while retaining the applicable collection-nullability contract.
internal sealed class NativeGraphKeyComparer : IEqualityComparer<NativeGraphKey>
{
    internal static readonly NativeGraphKeyComparer Instance = new();

    public bool Equals(NativeGraphKey left, NativeGraphKey right)
        => ReferenceEquals(left.Value, right.Value)
            && EqualityComparer<NativeValueValidation?>.Default.Equals(left.Validation, right.Validation);

    public int GetHashCode(NativeGraphKey value)
        => HashCode.Combine(RuntimeHelpers.GetHashCode(value.Value), value.Validation);
}
