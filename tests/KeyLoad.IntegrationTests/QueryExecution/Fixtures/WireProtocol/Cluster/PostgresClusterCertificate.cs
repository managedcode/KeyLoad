using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresClusterCertificate(string Directory, string RootCertificate)
{
    private const string DirectoryName = "native-sql";
    private const string RootName = "root.pem";
    private const string ServerName = "server.pfx";
    internal string ServerPath => Path.Combine(Directory, ServerName);
    private const string RootSubject = "CN=KeyLoad Orleans native SQL test root";
    private const string ServerSubject = "CN=localhost";
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const int SerialBytes = 16;
    internal static PostgresClusterCertificate Create(string root)
        => Create(root, Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
            .Select(ClusterFixtureProtocol.NodeName));
    internal static PostgresClusterCertificate Create(string root, IEnumerable<string> nodes)
    {
        var directory = Path.Combine(root, DirectoryName);
        System.IO.Directory.CreateDirectory(directory);
        var rootPath = Path.Combine(directory, RootName);
        var failures = new List<Exception>();
        global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
        {
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
            {
                var now = TimeProvider.System.GetUtcNow();
                var request = new CertificateRequest(RootSubject, rootKey, HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, default, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
                using var authority = request.CreateSelfSigned(now.AddMinutes(-1), now.AddDays(1));
                global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
                {
                    using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
                    {
                        var server = new CertificateRequest(ServerSubject, serverKey, HashAlgorithmName.SHA256);
                        server.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, default, true));
                        server.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                        server.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthentication)], false));
                        var names = new SubjectAlternativeNameBuilder();
                        names.AddDnsName("localhost");
                        names.AddIpAddress(IPAddress.Loopback);
                        names.AddIpAddress(IPAddress.IPv6Loopback);
                        foreach (var node in nodes)
                        {
                            names.AddDnsName(node);
                        }
                        server.CertificateExtensions.Add(names.Build());
                        using var issued = server.Create(authority, now.AddMinutes(-1), now.AddDays(1),
                            RandomNumberGenerator.GetBytes(SerialBytes));
                        global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
                        {
                            using var withKey = issued.CopyWithPrivateKey(serverKey);
                            global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
                            {
                                File.WriteAllText(rootPath, authority.ExportCertificatePem(), Encoding.ASCII);
                                var encoded = withKey.Export(X509ContentType.Pfx);
                                try
                                {
                                    File.WriteAllBytes(Path.Combine(directory, ServerName), encoded);
                                }
                                finally
                                {
                                    CryptographicOperations.ZeroMemory(encoded);
                                }
                            }, failures);
                        }, failures);
                    }, failures);
                }, failures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return new(directory, rootPath);
    }
    internal static string RootPath(ClusterFixture fixture)
        => Path.Combine(fixture.Root, DirectoryName, RootName);
}

