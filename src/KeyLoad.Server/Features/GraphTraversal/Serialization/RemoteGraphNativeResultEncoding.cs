using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeResultEncoding
{
    internal static ReadOnlyMemory<byte> Encode<T>(T actual, int maximumBytes, ReadExecutionBudget work)
    {
        work.Check();
        var measured = NativeSerialization.Measure(actual);
        if (measured > maximumBytes || measured > work.MaximumResultBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
        work.ChargeBytes(measured);
        var encoded = NativeSerialization.Serialize(actual);
        if (encoded.Length == measured)
        { return encoded; }
        CryptographicOperations.ZeroMemory(encoded);
        throw Errors.Fail(ErrorCode.Corruption, RemoteGraphNativeProtocol.InvalidProof);
    }
}
