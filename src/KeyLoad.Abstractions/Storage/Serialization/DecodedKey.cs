using System.Collections;

namespace KeyLoad.Storage;

/// <summary>A typed view of the compact native scalar slots decoded from one canonical key.</summary>
public readonly record struct DecodedKey : IReadOnlyList<KeyComponent>
{
    private readonly object?[]? /* KLD0041: Native key tags define a closed heterogeneous scalar set; reference-sized slots avoid a large scalar struct for every text field. */ values;

    internal DecodedKey(object?[] /* KLD0041: Only the canonical decoder constructs these owned slots from validated native scalar tags. */ values)
        => this.values /* KLD0041: This private array retains the exact native scalars and is never exposed as an untyped public contract. */ = values;

    /// <summary>Gets the number of decoded components.</summary>
    public int Count => values /* KLD0041: Only the native slot count is observed here; no payload conversion occurs. */?.Length ?? 0;

    /// <summary>Gets the number of decoded components.</summary>
    public int Length => Count;

    /// <summary>Gets one component with its closed scalar kind and typed value accessors.</summary>
    /// <param name="index">The zero-based component index.</param>
    /// <returns>The selected component.</returns>
    public KeyComponent this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return values /* KLD0041: The decoder owns these compact scalar slots; this indexer restores their closed typed view. */![index] switch
            {
                null => KeyComponent.Null,
                MissingValue => KeyComponent.Missing,
                string text /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromString(text),
                byte[] bytes /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromByteArray(bytes),
                bool flag /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromBoolean(flag),
                long number /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromInt64(number),
                decimal number /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromDecimal(number),
                double number /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromDouble(number),
                DateTimeOffset timestamp /* KLD0042: The canonical decoder admitted this exact native scalar kind before this typed projection. */ => KeyComponent.FromDateTimeOffset(timestamp),
                _ => throw new InvalidOperationException(nameof(DecodedKey))
            };
        }
    }

    /// <summary>Enumerates typed components in key order.</summary>
    /// <returns>The typed enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyComponent> IEnumerable<KeyComponent>.GetEnumerator() => GetEnumerator();
    IEnumerator /* KLD0041: IEnumerable fixes this interface bridge; normal callers use the typed enumerator. */ IEnumerable.GetEnumerator()
        => GetEnumerator();

    /// <summary>Enumerates one decoded key without allocating an iterator on the typed path.</summary>
    public struct Enumerator : IEnumerator<KeyComponent>
    {
        private readonly DecodedKey key;
        private int index;

        internal Enumerator(DecodedKey key)
        {
            this.key = key;
            index = -1;
        }

        /// <inheritdoc />
        public readonly KeyComponent Current => index >= 0 && index < key.Count
            ? key[index] : throw new InvalidOperationException(nameof(Enumerator));

        readonly object /* KLD0041: IEnumerator requires object; only the non-generic CLR bridge boxes its current component. */ IEnumerator.Current
            => Current /* KLD0042: The non-generic IEnumerator contract requires a boxed scalar component; typed iteration uses KeyComponent directly. */;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (index >= key.Count)
            {
                return false;
            }
            return ++index < key.Count;
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }

        /// <inheritdoc />
        public readonly void Reset() => throw new NotSupportedException();
    }
}
