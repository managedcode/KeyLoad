using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartialCurrentNodeLayoutPrefix
{
    internal static async Task<string> CreateAsync(PartitionHostRecoveryFixture fixture)
    {
        Directory.CreateDirectory(fixture.Options.DataDirectory);
        var key = Convert.FromBase64String(fixture.Options.SigningKey);
        string? identity = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = new ZoneTreeStore(new(Path(fixture))
                {
                    Incarnation = fixture.Options.Incarnation,
                    SigningKey = key
                },
                    UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(store.Position).IsEqualTo(0L);
                    await Assert.That(PartialCurrentNodeLayoutState.Rows(store)).IsEmpty();
                    await Assert.That(store.Identity.Incarnation).IsEqualTo(fixture.Options.Incarnation);
                    await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(key)).IsTrue();
                    identity = JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
                }, failures);
            }, failures);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        ServerFailureObserver.ThrowIfAny(failures);
        return identity ?? throw new InvalidOperationException(MissingIdentity);
    }

    internal static async Task RequireAsync(PartitionHostRecoveryFixture fixture, string identity)
    {
        var key = Convert.FromBase64String(fixture.Options.SigningKey);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = new ZoneTreeStore(new(Path(fixture))
                {
                    Incarnation = fixture.Options.Incarnation,
                    SigningKey = key
                },
                    UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
                await ServerFailureObserver.ObserveAsync(async () =>
                    await Assert.That(JsonSerializer.Serialize(store.Identity, JsonDefaults.Options)).IsEqualTo(identity), failures);
            }, failures);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string Path(PartitionHostRecoveryFixture fixture)
        => System.IO.Path.Combine(fixture.Options.DataDirectory, PartitionHostRecoveryFixture.CanonicalDirectory);
    private const string MissingIdentity = "The genuine empty execution prefix identity is required.";
}
