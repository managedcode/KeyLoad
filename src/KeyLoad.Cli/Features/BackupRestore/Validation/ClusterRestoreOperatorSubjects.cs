using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Binds fresh native persisted operator observations to the complete original owner union.</summary>
internal static class ClusterRestoreOperatorSubjects
{
    private const string Invalid = "The current native operator does not match the original per-owner restore plan.";

    internal static ClusterRestoreOperatorSubject Read(IKeyValueView view, Guid owner, string credential,
        TimeProvider clock)
    {
        var principal = ClusterRestoreOperatorValidation.RequireAdministrator(view, credential, clock.GetUtcNow());
        var bytes = Encoding.UTF8.GetBytes(credential);
        try
        {
            return new(ClusterRestoreOperatorSubject.CurrentVersion, owner, principal.Id, principal.PolicyEpoch,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static void Require(ImmutableArray<ClusterRestoreOperatorSubject> original,
        ImmutableArray<ClusterRestoreOperatorSubject> actual, ImmutableArray<ClusterBackupOwnerCut> cuts)
    {
        if (original.IsDefault || actual.IsDefault || original.Length != cuts.Length || actual.Length != cuts.Length
            || original.Select(subject => subject.SourceOwnerId).Distinct().Count() != cuts.Length)
        { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
        foreach (var pair in original.Zip(actual).Zip(cuts))
        {
            var expected = pair.First.First;
            if (expected.Version != ClusterRestoreOperatorSubject.CurrentVersion
                || expected.SourceOwnerId != pair.Second.Owner.PhysicalShardId || expected != pair.First.Second)
            { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
        }
    }
}
