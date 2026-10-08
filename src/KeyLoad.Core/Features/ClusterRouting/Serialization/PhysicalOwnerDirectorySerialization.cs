using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PhysicalOwnerDirectorySerialization
{
    private const string Space = "physical-owner-directory";
    private const string VersionKey = "v1";

    internal static byte[] Key() => KeyCodec.Encode(Space, VersionKey);

    internal static PhysicalOwnerDirectoryV1? Read(IKeyValueView view)
    {
        PhysicalOwnerDirectoryV1? value = null;
        view.ReadValue(Key(), bytes => value = Decode(bytes));
        return value;
    }

    internal static PhysicalOwnerDirectoryV1? Read(IKeyValueView view, ReadExecutionBudgetReadGrant grant)
    {
        PhysicalOwnerDirectoryV1? value = null;
        grant.ReadValue(view, Key(), bytes => value = Decode(bytes));
        return value;
    }

    private static PhysicalOwnerDirectoryV1 Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > PhysicalOwnerDirectoryProtocol.MaximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.Malformed); }
        try
        {
            var value = NativeSerialization.Deserialize<PhysicalOwnerDirectoryV1>(bytes);
            PhysicalOwnerDirectoryValidation.Validate(value);
            return value;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.FormatUnsupported)
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.Malformed); }
    }

    internal static byte[] Encode(PhysicalOwnerDirectoryV1 value)
    {
        PhysicalOwnerDirectoryValidation.Validate(value);
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length > PhysicalOwnerDirectoryProtocol.MaximumEncodedBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PhysicalOwnerDirectoryProtocol.Capacity); }
        return bytes;
    }
}
