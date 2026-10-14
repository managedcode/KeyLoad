using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorCoverageCaptureBuffers
{
    internal static void Clear(EventVectorCoverageCapture? capture)
    {
        if (capture is null)
        {
            return;
        }
        Clear(capture.OriginalEncodedEntries);
        Clear(capture.EntryChecksum);
        Clear(capture.PhysicalCatalog);
        Clear(capture.RegisteredOwnerRows);
        Clear(capture.PlacementRows);
        Clear(capture.LogicalDirectory);
        Clear(capture.AtomicPartitionRosterRows);
        Clear(capture.SourceHeadRows);
        Clear(capture.RosterOriginRows);
        Clear(capture.RosterRestoreIdentity);
    }

    private static void Clear(EventVectorCoverageRow[]? rows)
    {
        if (rows is null)
        {
            return;
        }
        foreach (var row in rows)
        {
            Clear(row);
        }
    }

    private static void Clear(EventVectorCoverageRow? row)
    {
        if (row is null)
        {
            return;
        }
        Clear(row.Key);
        Clear(row.Value);
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
