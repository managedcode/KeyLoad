using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

internal static class PhysicalOwnerRegistrationIdentity
{
    private const string CommandDomain = "KeyLoad.PhysicalOwnerDirectory.Register.v1\0";
    private const int GuidBytes = 16;
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(CommandDomain);

    internal static Guid Create(RegisterPhysicalOwnerV1 request)
    {
        PhysicalOwnerDirectoryValidation.ValidateRegistration(request);
        var payload = NativeSerialization.Serialize(request);
        if (payload.Length > PhysicalOwnerDirectoryProtocol.MaximumRegistrationBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PhysicalOwnerDirectoryProtocol.Capacity); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        hash.AppendData(payload);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return new Guid(digest[..GuidBytes], bigEndian: true);
    }
}
