using System.Numerics;

namespace KeyLoad.Core.Features.ClusterRouting;

internal static class PlacementRendezvousScore
{
    private const int MinimumWeight = 1;
    private const int DigestBitCount = 64;
    private const int MaximumWeight = 16;
    private static readonly BigInteger Denominator = (BigInteger.One << DigestBitCount) + BigInteger.One;

    internal static int Compare(ulong first, int firstWeight, ulong second, int secondWeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstWeight, MinimumWeight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstWeight, MaximumWeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(secondWeight, MinimumWeight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secondWeight, MaximumWeight);
        var firstNumerator = new BigInteger(first) + BigInteger.One;
        var secondNumerator = new BigInteger(second) + BigInteger.One;
        var left = BigInteger.Pow(firstNumerator, secondWeight) * BigInteger.Pow(Denominator, firstWeight);
        var right = BigInteger.Pow(secondNumerator, firstWeight) * BigInteger.Pow(Denominator, secondWeight);
        return left.CompareTo(right);
    }
}
