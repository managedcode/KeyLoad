using System.Security.Cryptography;
using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal bool RemoteGraphIncomingBytesEqual<T>(T left, T right, ReadExecutionBudget work)
    {
        work.Check();
        var firstLength = NativeSerialization.Measure(left);
        var secondLength = NativeSerialization.Measure(right);
        if (firstLength > Limits.MaxBatchBytes || secondLength > Limits.MaxBatchBytes
            || firstLength > work.MaximumResultBytes || secondLength > work.MaximumResultBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid); }
        work.ChargeBytes(checked(firstLength + secondLength));
        var first = NativeSerialization.Serialize(left);
        try
        {
            var second = NativeSerialization.Serialize(right);
            try
            { work.Check(); return first.AsSpan().SequenceEqual(second); }
            finally { CryptographicOperations.ZeroMemory(second); }
        }
        finally { CryptographicOperations.ZeroMemory(first); }
    }
}
