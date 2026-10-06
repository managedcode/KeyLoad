using Microsoft.Extensions.Options;
using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;

internal static class ClusterProfileOfflineUpgrade
{
    private const int LegacyRequiredFields = 4;
    private static readonly string[] LegacyFields =
    [nameof(LegacyLocalProfile.Incarnation), nameof(LegacyLocalProfile.SigningKey),
        nameof(LegacyLocalProfile.PeerSecret), nameof(LegacyLocalProfile.AdminKey)];

    internal static LocalProfile Run(string dataRoot, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        const int VersionValue = 2;

        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var root = ClusterProfileInputBounds.Root(dataRoot, executionOptions);
        ClusterProfileStore.RejectLinks(root);
        var path = Path.Combine(root, ClusterProfileStore.ProfileName);
        var backupPath = Path.Combine(root, ClusterProfileStore.LegacyBackupName);
        ClusterProfileStore.RejectLinks(path);
        ClusterProfileStore.RejectLinks(backupPath);
        if (!File.Exists(path) || File.Exists(backupPath))
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }

        ClusterProfilePermissions.RequirePrivate(path);
        var fileMode = ClusterProfilePermissions.ReadMode(path);
        var originalBytes = ClusterProfileStore.ReadBoundedBytes(path, executionOptions);
        var legacy = DeserializeLegacy(originalBytes, executionOptions);
        var profile = new LocalProfile(VersionValue, Guid.NewGuid(), legacy.Incarnation,
            legacy.SigningKey, legacy.PeerSecret, legacy.AdminKey);
        ClusterProfileStore.Validate(profile);
        return Publish(path, backupPath, originalBytes, profile, fileMode, executionOptions);
    }

    private static LocalProfile Publish(string path, string backupPath, byte[] originalBytes,
        LocalProfile profile, UnixFileMode? fileMode, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        var backupStage = ClusterProfileStore.StagingPath(backupPath);
        var profileStage = ClusterProfileStore.StagingPath(path);
        try
        {
            StageAndVerifyBackup(backupStage, originalBytes, fileMode, executionOptions);
            StageAndVerifyProfile(profileStage, profile, fileMode, executionOptions);
            File.Move(backupStage, backupPath);
            ClusterProfileStore.VerifyCopy(originalBytes, ClusterProfileStore.ReadBoundedBytes(backupPath, executionOptions));
            ClusterProfilePermissions.EnsureMode(backupPath, fileMode);
            File.Move(profileStage, path, overwrite: true);
            var published = ClusterProfileStore.DeserializeCurrent(ClusterProfileStore.ReadBoundedBytes(path, executionOptions), executionOptions);
            if (published != profile)
            { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
            ClusterProfilePermissions.EnsureMode(path, fileMode);
            return published;
        }
        finally
        {
            ClusterProfileStore.DeleteStage(backupStage);
            ClusterProfileStore.DeleteStage(profileStage);
        }
    }

    private static void StageAndVerifyBackup(string stage, byte[] originalBytes, UnixFileMode? fileMode, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        ClusterProfileStore.WriteStage(stage, originalBytes, fileMode, executionOptions);
        ClusterProfileStore.VerifyCopy(originalBytes, ClusterProfileStore.ReadBoundedBytes(stage, executionOptions));
        ClusterProfilePermissions.EnsureMode(stage, fileMode);
    }

    private static void StageAndVerifyProfile(string stage, LocalProfile profile, UnixFileMode? fileMode, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, ClusterProfileStore.CreateJson(executionOptions));
        ClusterProfileStore.WriteStage(stage, bytes, fileMode, executionOptions);
        if (ClusterProfileStore.DeserializeCurrent(ClusterProfileStore.ReadBoundedBytes(stage, executionOptions), executionOptions) != profile)
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
        ClusterProfilePermissions.EnsureMode(stage, fileMode);
    }

    private static LegacyLocalProfile DeserializeLegacy(byte[] bytes, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        try
        {
            ClusterProfileStore.RequireFields(bytes, LegacyFields, LegacyRequiredFields, executionOptions);
            var profile = JsonSerializer.Deserialize<LegacyLocalProfile>(bytes, ClusterProfileStore.CreateJson(executionOptions))
                ?? throw new InvalidOperationException(ClusterProfileStore.InvalidProfile);
            ClusterProfileStore.ValidateCredentials(profile.Incarnation, profile.SigningKey,
                profile.PeerSecret, profile.AdminKey);
            return profile;
        }
        catch (JsonException) { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
    }

    private sealed record LegacyLocalProfile(Guid Incarnation, string SigningKey, string PeerSecret, string AdminKey);
}
