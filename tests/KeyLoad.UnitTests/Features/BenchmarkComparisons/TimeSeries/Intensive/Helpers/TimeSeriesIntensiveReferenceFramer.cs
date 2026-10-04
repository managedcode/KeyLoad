using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveReferenceFramer : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    internal TimeSeriesIntensiveReferenceFramer(string domain)
    {
        Text("domain");
        Text(domain);
        Text("version");
        Text("1");
    }

    internal void Text(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Count(bytes.Length);
        hash.AppendData(bytes);
    }

    internal void Count(int value) => hash.AppendData([
        (byte)((uint)value >> 24), (byte)((uint)value >> 16), (byte)((uint)value >> 8), (byte)value]);

    internal void Number(long value)
    {
        var bits = unchecked((ulong)value);
        hash.AppendData([(byte)(bits >> 56), (byte)(bits >> 48), (byte)(bits >> 40), (byte)(bits >> 32),
            (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits]);
    }

    internal void Field(string label, string value)
    {
        Text(label);
        Text(value);
    }

    internal void Field(string label, long value)
    {
        Text(label);
        Number(value);
    }

    internal void Optional(string label, long? value)
    {
        Text(label);
        Present(value.HasValue);
        if (value.HasValue)
        {
            Number(value.Value);
        }
    }

    internal void Present(bool present) => hash.AppendData([present ? (byte)1 : (byte)0]);

    internal string Finish() => Convert.ToHexStringLower(hash.GetHashAndReset());

    public void Dispose() => hash.Dispose();
}
