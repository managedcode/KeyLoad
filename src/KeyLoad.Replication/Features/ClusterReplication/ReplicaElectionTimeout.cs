using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace KeyLoad.Replication;

internal static class ReplicaElectionTimeout
{
    internal static TimeSpan Select(ReplicaConfiguration configuration)
    {
        var lower = configuration.LowerElectionTimeout.Ticks;
        var width = checked((ulong)(configuration.UpperElectionTimeout.Ticks - lower));
        var mask = BitOperations.RoundUpToPowerOf2(width) - 1;
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        ulong sample;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            sample = BinaryPrimitives.ReadUInt64LittleEndian(bytes) & mask;
        }
        while (sample >= width);
        return TimeSpan.FromTicks(checked(lower + (long)sample));
    }
}
