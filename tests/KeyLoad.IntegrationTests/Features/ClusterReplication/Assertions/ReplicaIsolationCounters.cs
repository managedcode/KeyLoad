using System.Globalization;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ReplicaIsolationFlowProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Requires actual native DROP traffic counters after newer-majority ACK and former-leader refusal.</summary>
internal static class ReplicaIsolationCounters
{
    internal static void RequireObserved(string input, string output)
    {
        var packets = ReadPackets(input).Concat(ReadPackets(output)).ToArray();
        if (!packets.Any(value => value > Zero))
        { throw new InvalidOperationException("The native isolation rules did not observe actual blocked replica traffic."); }
    }

    private static IEnumerable<ulong> ReadPackets(string native)
    {
        var lines = native.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(HeaderRows))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length <= ByteColumn || !ulong.TryParse(columns[PacketColumn], NumberStyles.None,
                CultureInfo.InvariantCulture, out var packets) || !ulong.TryParse(columns[ByteColumn], NumberStyles.None,
                CultureInfo.InvariantCulture, out _))
            { throw new InvalidOperationException("Native filter traffic counters have an unsupported shape."); }
            yield return packets;
        }
    }
}
