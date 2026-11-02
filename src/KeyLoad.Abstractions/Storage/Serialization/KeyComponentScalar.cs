using System.Runtime.InteropServices;

namespace KeyLoad.Storage;

/// <summary>Overlays unmanaged scalar representations; the component kind selects the initialized value.</summary>
[StructLayout(LayoutKind.Explicit)]
internal readonly struct KeyComponentScalar
{
    private const int ScalarOffset = 0;
    [FieldOffset(ScalarOffset)]
    internal readonly long RawBits;
    [FieldOffset(ScalarOffset)]
    internal readonly decimal ExactNumber;

    internal KeyComponentScalar(long bits)
    {
        this = default;
        RawBits = bits;
    }

    internal KeyComponentScalar(decimal number)
    {
        this = default;
        ExactNumber = number;
    }
}
