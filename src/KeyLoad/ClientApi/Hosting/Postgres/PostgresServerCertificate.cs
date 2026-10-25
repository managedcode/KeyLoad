using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresServerCertificate : IDisposable
{
    internal PostgresServerCertificate(IOptions<PostgresSessionOptions> options)
    {
        var settings = options.Value;
        settings.Validate();
        Value = X509CertificateLoader.LoadPkcs12FromFile(settings.CertificatePath, null, KeyStorage);
        if (!Value.HasPrivateKey)
        {
            var failures = new List<Exception>
            {
                new OptionsValidationException(PostgresSessionOptions.Section, typeof(PostgresSessionOptions), [CertificateInvalid])
            };
            ServerFailureObserver.Observe(Value.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    internal X509Certificate2 Value
    {
        get;
    }
    public void Dispose() => Value.Dispose();

    // macOS cannot hold a PKCS#12 private key as an ephemeral key set; there the key lives in a temporary keychain
    // that the certificate removes on disposal.
    private static X509KeyStorageFlags KeyStorage => OperatingSystem.IsMacOS()
        ? X509KeyStorageFlags.DefaultKeySet
        : X509KeyStorageFlags.EphemeralKeySet;
    private const string CertificateInvalid = "Execution SQL requires an owned TLS certificate with a private key.";
}
