namespace KeyLoad.Storage;

/// <summary>One typed key component. Implicit conversions let callers pass values to <see cref="KeyCodec.Encode(ReadOnlySpan{KeyComponent})"/>
/// without boxing while preserving the canonical encoded bytes.</summary>
public readonly struct KeyComponent : IEquatable<KeyComponent>
{
    private const string GuidKeyFormat = "N";
    private const string BooleanRequired = "The component does not contain a boolean.";
    private const string WholeNumberRequired = "The component does not contain an integer.";
    private const string ExactNumberRequired = "The component does not contain an exact number.";
    private const string RealNumberRequired = "The component does not contain a real number.";
    private const string TimestampRequired = "The component does not contain a timestamp.";
    private const string TextRequired = "The component does not contain text.";
    private const string BinaryRequired = "The component does not contain binary bytes.";
    private readonly KeyComponentScalar scalar;
    private readonly object? /* KLD0041: This closed string-or-byte-array reference union shares storage without boxing scalar values; Kind guards each typed read. */ reference;

    private KeyComponent(KeyComponentKind kind, long bits = 0, decimal exact = 0, string? text = null, byte[]? binary = null)
    {
        Kind = kind;
        scalar = kind == KeyComponentKind.FixedPoint ? new(exact) : new(bits);
        reference /* KLD0041: The private constructor admits only the original string or byte-array reference into this closed union; scalar values use unmanaged storage. */ = kind == KeyComponentKind.Binary ? binary : text;
    }

    /// <summary>Gets the component's closed scalar kind.</summary>
    public KeyComponentKind Kind { get; }
    /// <summary>Gets a boolean component's value.</summary>
    public bool Boolean => Kind == KeyComponentKind.Boolean ? scalar.RawBits != 0 : throw new InvalidOperationException(BooleanRequired);
    /// <summary>Gets an integer component's normalized 64-bit value.</summary>
    public long WholeNumber => Kind == KeyComponentKind.WholeNumber ? scalar.RawBits : throw new InvalidOperationException(WholeNumberRequired);
    /// <summary>Gets a decimal component's exact value.</summary>
    public decimal ExactNumber => Kind == KeyComponentKind.FixedPoint ? scalar.ExactNumber : throw new InvalidOperationException(ExactNumberRequired);
    /// <summary>Gets a double component's finite value.</summary>
    public double RealNumber => Kind == KeyComponentKind.FloatingPoint ? BitConverter.Int64BitsToDouble(scalar.RawBits) : throw new InvalidOperationException(RealNumberRequired);
    /// <summary>Gets a timestamp component's UTC instant.</summary>
    public DateTimeOffset Timestamp => Kind == KeyComponentKind.Timestamp ? new(scalar.RawBits, TimeSpan.Zero) : throw new InvalidOperationException(TimestampRequired);
    internal long UtcTicks => scalar.RawBits;
    /// <summary>Gets a text component's original content.</summary>
    public string Text => Kind == KeyComponentKind.Text ? (string)reference /* KLD0041: The closed reference union holds a string for the validated Text kind. */! /* KLD0042: Kind and the private constructor guarantee this reference is a string, without boxing. */ : throw new InvalidOperationException(TextRequired);
    /// <summary>Gets the binary component's bytes without exposing a mutable array contract.</summary>
    public ReadOnlyMemory<byte> Bytes => Kind == KeyComponentKind.Binary ? Binary : throw new InvalidOperationException(BinaryRequired);
    internal byte[] Binary => (byte[])reference /* KLD0041: Only the Binary kind reads this closed byte-array reference slot. */! /* KLD0042: The private binary factory places the original byte-array reference in this union without copying or boxing it. */;

    /// <summary>Gets the explicit null component.</summary>
    public static KeyComponent Null { get; } = new(KeyComponentKind.Null);

    /// <summary>Gets the absent (missing) component, which sorts before null.</summary>
    public static KeyComponent Missing { get; } = new(KeyComponentKind.Missing);

    /// <summary>Creates a text component; a null string is the null component.</summary>
    /// <param name="value">The text, or null.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromString(string? value) => value is null ? Null : new(KeyComponentKind.Text, text: value);

    /// <summary>Creates a binary component; a null array is the null component.</summary>
    /// <param name="value">The bytes, or null.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromByteArray(byte[]? value) => value is null ? Null : new(KeyComponentKind.Binary, binary: value);

    /// <summary>Creates a 64-bit integer component.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromInt64(long value) => new(KeyComponentKind.WholeNumber, value);

    /// <summary>Creates a boolean component.</summary>
    /// <param name="value">The flag.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromBoolean(bool value) => new(KeyComponentKind.Boolean, value ? 1 : 0);

    /// <summary>Creates a decimal component.</summary>
    /// <param name="value">The exact number.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromDecimal(decimal value) => new(KeyComponentKind.FixedPoint, exact: value);

    /// <summary>Creates a double component; non-finite values are rejected when encoded.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromDouble(double value) => new(KeyComponentKind.FloatingPoint, BitConverter.DoubleToInt64Bits(value));

    /// <summary>Creates a timestamp component ordered by its UTC instant.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromDateTimeOffset(DateTimeOffset value) => new(KeyComponentKind.Timestamp, value.UtcTicks);

    /// <summary>Creates a 64-bit integer component from a 32-bit integer; both encode identically.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromInt32(int value) => FromInt64(value);

    /// <summary>Creates the missing component from the marker; a null marker is the null component.</summary>
    /// <param name="value">The marker, or null.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromMissingValue(MissingValue? value) => value is null ? Null : Missing;

    /// <summary>Creates an identifier component encoded as its 32-character hexadecimal text.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The component.</returns>
    public static KeyComponent FromGuid(Guid value) => FromString(value.ToString(GuidKeyFormat));

    /// <summary>Converts a text component.</summary>
    /// <param name="value">The text, or null.</param>
    public static implicit operator KeyComponent(string? value) => FromString(value);

    /// <summary>Converts a binary component.</summary>
    /// <param name="value">The bytes, or null.</param>
    public static implicit operator KeyComponent(byte[]? value) => FromByteArray(value);

    /// <summary>Converts a 64-bit integer component.</summary>
    /// <param name="value">The integer.</param>
    public static implicit operator KeyComponent(long value) => FromInt64(value);

    /// <summary>Converts a 32-bit integer; it is encoded exactly like the same 64-bit value.</summary>
    /// <param name="value">The integer.</param>
    public static implicit operator KeyComponent(int value) => FromInt32(value);

    /// <summary>Converts a boolean component.</summary>
    /// <param name="value">The flag.</param>
    public static implicit operator KeyComponent(bool value) => FromBoolean(value);

    /// <summary>Converts a decimal component.</summary>
    /// <param name="value">The exact number.</param>
    public static implicit operator KeyComponent(decimal value) => FromDecimal(value);

    /// <summary>Converts a double component.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator KeyComponent(double value) => FromDouble(value);

    /// <summary>Converts a timestamp component.</summary>
    /// <param name="value">The instant.</param>
    public static implicit operator KeyComponent(DateTimeOffset value) => FromDateTimeOffset(value);

    /// <summary>Converts an identifier component.</summary>
    /// <param name="value">The identifier.</param>
    public static implicit operator KeyComponent(Guid value) => FromGuid(value);

    /// <summary>Converts the missing-value marker; a null marker is the null component.</summary>
    /// <param name="value">The marker, or null.</param>
    public static implicit operator KeyComponent(MissingValue? value) => FromMissingValue(value);

    /// <inheritdoc />
    public bool Equals(KeyComponent other)
        => Kind == other.Kind && Kind switch
        {
            KeyComponentKind.FixedPoint => ExactNumber == other.ExactNumber,
            KeyComponentKind.Text => string.Equals(Text, other.Text, StringComparison.Ordinal),
            KeyComponentKind.Binary => Binary.AsSpan().SequenceEqual(other.Binary),
            _ => scalar.RawBits == other.scalar.RawBits
        };

    /// <inheritdoc />
    public override bool Equals(object? /* KLD0041: System.ValueType.Equals fixes this argument to object; typed key equality uses IEquatable directly. */ obj)
        => obj is KeyComponent other /* KLD0042: The CLR equality override must extract its key component before calling typed equality. */ && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        KeyComponentKind.Text => HashCode.Combine(Kind, string.GetHashCode(Text, StringComparison.Ordinal)),
        KeyComponentKind.Binary => HashCode.Combine(Kind, Binary.Length),
        KeyComponentKind.FixedPoint => HashCode.Combine(Kind, ExactNumber),
        _ => HashCode.Combine(Kind, scalar.RawBits)
    };

    /// <summary>Compares two components for equality.</summary>
    /// <param name="left">The first component.</param>
    /// <param name="right">The second component.</param>
    /// <returns>True when both encode identically.</returns>
    public static bool operator ==(KeyComponent left, KeyComponent right) => left.Equals(right);

    /// <summary>Compares two components for inequality.</summary>
    /// <param name="left">The first component.</param>
    /// <param name="right">The second component.</param>
    /// <returns>True when the components differ.</returns>
    public static bool operator !=(KeyComponent left, KeyComponent right) => !left.Equals(right);
}
