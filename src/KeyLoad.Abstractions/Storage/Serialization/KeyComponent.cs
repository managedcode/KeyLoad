namespace KeyLoad.Storage;

/// <summary>One typed key component. Implicit conversions let callers pass values to <see cref="KeyCodec.Encode(ReadOnlySpan{KeyComponent})"/>
/// without boxing while preserving the canonical encoded bytes.</summary>
public readonly struct KeyComponent : IEquatable<KeyComponent>
{
    private const string GuidKeyFormat = "N";
    private readonly long bits;
    private readonly decimal exact;
    private readonly string? text;
    private readonly byte[]? binary;

    private KeyComponent(KeyComponentKind kind, long bits = 0, decimal exact = 0, string? text = null, byte[]? binary = null)
    {
        Kind = kind;
        this.bits = bits;
        this.exact = exact;
        this.text = text;
        this.binary = binary;
    }

    /// <summary>Gets the component's closed scalar kind.</summary>
    public KeyComponentKind Kind { get; }
    /// <summary>Gets a boolean component's value.</summary>
    public bool Boolean => Kind == KeyComponentKind.Boolean ? bits != 0 : throw new InvalidOperationException("The component does not contain a boolean.");
    /// <summary>Gets an integer component's normalized 64-bit value.</summary>
    public long Integer => Kind == KeyComponentKind.WholeNumber ? bits : throw new InvalidOperationException("The component does not contain an integer.");
    /// <summary>Gets a decimal component's exact value.</summary>
    public decimal ExactNumber => Kind == KeyComponentKind.FixedPoint ? exact : throw new InvalidOperationException("The component does not contain an exact number.");
    /// <summary>Gets a double component's finite value.</summary>
    public double RealNumber => Kind == KeyComponentKind.FloatingPoint ? BitConverter.Int64BitsToDouble(bits) : throw new InvalidOperationException("The component does not contain a real number.");
    /// <summary>Gets a timestamp component's UTC instant.</summary>
    public DateTimeOffset Timestamp => Kind == KeyComponentKind.Timestamp ? new(bits, TimeSpan.Zero) : throw new InvalidOperationException("The component does not contain a timestamp.");
    internal long UtcTicks => bits;
    /// <summary>Gets a text component's original content.</summary>
    public string Text => Kind == KeyComponentKind.Text ? text! : throw new InvalidOperationException("The component does not contain text.");
    /// <summary>Gets the binary component's bytes without exposing a mutable array contract.</summary>
    public ReadOnlyMemory<byte> Bytes => Kind == KeyComponentKind.Binary ? binary! : throw new InvalidOperationException("The component does not contain binary bytes.");
    internal byte[] Binary => binary!;

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
        => Kind == other.Kind && bits == other.bits && exact == other.exact && Kind switch
        {
            KeyComponentKind.Text => string.Equals(Text, other.Text, StringComparison.Ordinal),
            KeyComponentKind.Binary => Binary.AsSpan().SequenceEqual(other.Binary),
            _ => true
        };

    /// <inheritdoc />
    public override bool Equals(object? /* KLD0041: System.ValueType.Equals fixes this argument to object; typed key equality uses IEquatable directly. */ obj)
        => obj is KeyComponent other /* KLD0042: The CLR equality override must extract its key component before calling typed equality. */ && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        KeyComponentKind.Text => HashCode.Combine(Kind, string.GetHashCode(Text, StringComparison.Ordinal)),
        KeyComponentKind.Binary => HashCode.Combine(Kind, Binary.Length),
        _ => HashCode.Combine(Kind, bits, exact)
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
