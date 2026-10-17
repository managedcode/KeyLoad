using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains the initial live query snapshot and its continuation metadata.</summary>
/// <param name="Rows">The rows in the snapshot.</param>
/// <param name="Cursor">The cursor used to read subsequent changes.</param>
/// <param name="ThroughSequence">The outbox sequence through which the snapshot is consistent.</param>
/// <param name="CutPosition">The committed position at which the snapshot was taken.</param>
/// <remarks>The snapshot is complete within its configured row and byte bounds.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LiveQuerySnapshot)]
public sealed record LiveQuerySnapshot([property: Orleans.Id(0)] ImmutableArray<QueryRow> Rows, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] long CutPosition);
