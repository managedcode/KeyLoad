using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorWorkEncoding
{
    internal static byte[] Encode<T>(T value, EventVectorRemoteEncoding encoding, ReadExecutionBudget work)
    {
        var measured = NativeSerialization.Measure(value);
        work.ChargeBytes(measured);
        var bytes = encoding.Encode(value);
        if (bytes.Length == measured)
        { return bytes; }
        CryptographicOperations.ZeroMemory(bytes);
        throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }
}
