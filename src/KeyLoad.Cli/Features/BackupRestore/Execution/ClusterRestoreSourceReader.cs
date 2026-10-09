using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

internal static class ClusterRestoreSourceReader
{
    private const string Invalid = "The original cluster archive or supplied per-owner credential is inconsistent.";
    private const int SingleCredential = 1;
    internal sealed record Result(ImmutableArray<ClusterRestoreSource> Sources,
        ImmutableArray<ClusterRestoreOperatorSubject> Subjects, ImmutableArray<string> SourceSignerFingerprints);

    internal static Result Read(ClusterRestoreOperatorConfiguration options,
        IOptions<ZoneTreeStorageExecutionOptions> storage, IOptions<DatabaseLimits> limits,
        TimeProvider clock, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ClusterRestoreMappingValidation.RequireSourceCount(options.Sources.Length);
        var sources = ImmutableArray.CreateBuilder<ClusterRestoreSource>(options.Sources.Length);
        var subjects = ImmutableArray.CreateBuilder<ClusterRestoreOperatorSubject>(options.Sources.Length);
        var fingerprints = ImmutableArray.CreateBuilder<string>(options.Sources.Length);
        var seen = new HashSet<Guid>();
        foreach (var input in options.Sources)
        {
            work.Check();
            if (input is null || !seen.Add(input.OwnerId))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.ArchiveDirectory));
            ClusterRestorePathValidation.RequireAncestors(path);
            var candidates = options.Credentials.Where(value => value.SourceOwnerId == input.OwnerId).ToArray();
            if (candidates.Length != SingleCredential || string.IsNullOrWhiteSpace(candidates.First().Credential))
            { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
            var archive = ZoneTreeStore.ReadVerifiedCatalogBackup(path, storage, cancellationToken);
            if (archive.ManifestDigest != input.ExpectedManifestDigest)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            var actual = ZoneTreeStore.ReadVerifiedCatalogBackup(path, storage, (view, identity, position, metadata) =>
            {
                var cut = ClusterBackupNativeCutVerification.Require(view, identity, position, metadata,
                    candidates.First().Credential, limits, clock, work);
                if (cut.Owner.PhysicalShardId != input.OwnerId)
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
                var subject = ClusterRestoreOperatorSubjects.Read(view, input.OwnerId, candidates.First().Credential, clock);
                return (Cut: cut, Subject: subject, Fingerprint: Convert.ToHexStringLower(SHA256.HashData(identity.SigningKey.Span)));
            }, cancellationToken);
            var files = ClusterRestoreSourceFiles.Read(path, storage.Value, cancellationToken);
            var envelope = files.Single(file => file.Name == ClusterRestoreSourceFiles.EnvelopeName);
            sources.Add(new(ClusterRestoreSource.CurrentVersion, path, actual.Cut, files, envelope.Checksum));
            subjects.Add(actual.Subject);
            fingerprints.Add(actual.Fingerprint);
            work.Check();
        }
        return new(sources.ToImmutable(), subjects.ToImmutable(), fingerprints.ToImmutable());
    }
}
