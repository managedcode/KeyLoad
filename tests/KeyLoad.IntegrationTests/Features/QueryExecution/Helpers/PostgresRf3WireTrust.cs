using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3WireTrust
{
    internal static async Task AuthenticateAsync(SslStream stream, string host, string authorityPath, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var authority = X509Certificate2.CreateFromPemFile(authorityPath);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var trust = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck
                };
                trust.CustomTrustStore.Add(authority);
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    CertificateChainPolicy = trust
                }, token).ConfigureAwait(false);
                await global::TUnit.Assertions.Assert.That(stream.IsAuthenticated).IsTrue();
                await global::TUnit.Assertions.Assert.That(stream.IsEncrypted).IsTrue();
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
