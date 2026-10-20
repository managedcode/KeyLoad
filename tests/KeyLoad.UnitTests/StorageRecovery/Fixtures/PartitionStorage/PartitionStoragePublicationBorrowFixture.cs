using System.Security.Cryptography;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class PartitionStoragePublicationBorrowFixture : IAsyncDisposable
{
    private const string RootPrefix = "keyload-publication-borrow-";
    private const string GuidFormat = "N";
    private const string AdministratorPrefix = "root.";
    private const int SecretBytes = 32;
    private const int NoFailures = 0;
    private const string UnknownOwner = "The original execution publication owner has not joined; retain its root.";
    private readonly NodeOptions options;
    private PartitionHostApplicationOwner? owner;
    private RequestCqrsHostedRuntimeFixture? execution;
    private Task? closing;
    private bool retained;

    internal PartitionStoragePublicationBorrowFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        options = new()
        {
            DataDirectory = DirectoryPath,
            Incarnation = Guid.NewGuid(),
            SigningKey = Secret(),
            AdminKey = AdministratorPrefix + Secret()
        };
        options.Validate();
    }

    internal string DirectoryPath
    {
        get;
    }
    internal PartitionHostApplicationOwner Owner => owner ?? throw new ObjectDisposedException(nameof(PartitionStoragePublicationBorrowFixture));
    internal void RetainEvidence() => retained = true;

    internal async Task OpenAsync()
    {
        if (owner is not null)
        {
            await CloseOwnerAsync().ConfigureAwait(false);
        }
        if (execution is null)
        {
            execution = new();
            await execution.InitializeAsync(TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        }
        var runtime = ServerRuntimeTestOptions.Runtime(options);
        owner = new(runtime, new AuthorizationPolicy(), new CommandAdmissionGovernor(runtime.CommandAdmission),
            TimeProvider.System, NullLogger<PartitionHost>.Instance, execution.Grains);
        await owner.OpenAsync(execution.Services.GetRequiredService<PartitionHostApplicationOwner>().Session.OwnerContext.Context).ConfigureAwait(false);
    }

    private async Task CloseOwnerAsync()
    {
        if (owner is null)
        {
            return;
        }
        await owner.DisposeAsync().ConfigureAwait(false);
        if (!owner.CanReleaseRoot)
        {
            throw new InvalidOperationException(UnknownOwner);
        }
    }

    public ValueTask DisposeAsync() => new(closing ??= CloseCoreAsync());

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        if (owner is not null)
        {
            try
            {
                await owner.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            if (!owner.CanReleaseRoot)
            {
                failures.Add(new InvalidOperationException(UnknownOwner));
            }
        }
        await RuntimeCloseAsync(failures).ConfigureAwait(false);
        if (failures.Count == NoFailures && !retained && Directory.Exists(DirectoryPath))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(DirectoryPath, true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));

    private async Task RuntimeCloseAsync(List<Exception> failures)
    {
        try
        {
            if (execution is not null)
            {
                await execution.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

}
