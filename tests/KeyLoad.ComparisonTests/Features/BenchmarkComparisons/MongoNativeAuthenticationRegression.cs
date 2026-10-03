using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoNativeAuthenticationRegression
{
    private const string NodePrefix = "isolated-mongo-";
    private const string Bootstrap = "isolated-mongo-bootstrap";
    private const string Tcp = "tcp";
    private const string User = "benchmark", Database = "admin", AuthenticationMechanism = "SCRAM-SHA-256";
    private const string Ping = "ping", Ok = "ok", UsersInfo = "usersInfo", Users = "users";
    private const string UserField = "user", DatabaseField = "db", UserId = "userId";
    private const string ShowCredentials = "showCredentials", ShowCustomData = "showCustomData";
    private const string Credentials = "credentials", CustomData = "customData";
    private const int MinimumNodes = 1, MaximumNodes = 3, MaximumPool = 4, CommandEnabled = 1, UuidBytes = 16;
    private static readonly string[] NodeNames = ["isolated-mongo-1", "isolated-mongo-2", "isolated-mongo-3"];
    private static readonly TimeSpan NativeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>AC-ISO-002/003/006: authenticate every real selected node and compare persisted user UUIDs without exporting user documents.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, string password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, MinimumNodes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, MaximumNodes);
        var nodes = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>()
            .Where(resource => resource.Name.StartsWith(NodePrefix, StringComparison.Ordinal) && resource.Name != Bootstrap)
            .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
        await Assert.That(nodes.Select(node => node.Name)).IsEquivalentTo(NodeNames.Take(nodeCount));
        byte[]? expected = null;
        foreach (var node in nodes)
        {
            var observed = await VerifyNodeAsync(app.GetEndpoint(node.Name, Tcp), password, token);
            if (expected is null)
            {
                expected = observed;
            }
            else
            {
                await Assert.That(observed.AsSpan().SequenceEqual(expected)).IsTrue();
            }
        }
    }

    private static async Task<byte[]> VerifyNodeAsync(Uri endpoint, string password, CancellationToken token)
    {
        using var client = new MongoClient(CreateSettings(endpoint, password));
        var database = client.GetDatabase(Database);
        var ping = await database.RunCommandAsync<BsonDocument>(new BsonDocument(Ping, CommandEnabled), ReadPreference.Nearest, token);
        await Assert.That(CommandSucceeded(ping)).IsTrue();
        var command = new BsonDocument
        {
            [UsersInfo] = new BsonDocument { [UserField] = User, [DatabaseField] = Database },
            [ShowCredentials] = false,
            [ShowCustomData] = false
        };
        var result = await database.RunCommandAsync<BsonDocument>(command, ReadPreference.Nearest, token);
        await Assert.That(CommandSucceeded(result)).IsTrue();
        await Assert.That(result.GetValue(Users, BsonNull.Value).IsBsonArray).IsTrue();
        var users = result[Users].AsBsonArray;
        await Assert.That(users.Count).IsEqualTo(CommandEnabled);
        await Assert.That(users[0].IsBsonDocument).IsTrue();
        return await ReadIdentityAsync(users[0].AsBsonDocument);
    }

    private static async Task<byte[]> ReadIdentityAsync(BsonDocument user)
    {
        await Assert.That(user.GetValue(UserField, BsonNull.Value) == User).IsTrue();
        await Assert.That(user.GetValue(DatabaseField, BsonNull.Value) == Database).IsTrue();
        await Assert.That(user.Contains(Credentials)).IsFalse();
        await Assert.That(user.Contains(CustomData)).IsFalse();
        await Assert.That(user.GetValue(UserId, BsonNull.Value).IsBsonBinaryData).IsTrue();
        var identity = user[UserId].AsBsonBinaryData;
        await Assert.That(identity.SubType).IsEqualTo(BsonBinarySubType.UuidStandard);
        await Assert.That(identity.Bytes.Length).IsEqualTo(UuidBytes);
        return identity.Bytes.ToArray();
    }

    private static bool CommandSucceeded(BsonDocument result)
    {
        var value = result.GetValue(Ok, BsonNull.Value);
        return value.IsNumeric && value.ToDouble() == CommandEnabled;
    }

    private static MongoClientSettings CreateSettings(Uri endpoint, string password)
    {
        var persisted = MongoCredential.CreateCredential(Database, User, password);
        return new MongoClientSettings
        {
            Server = new MongoServerAddress(endpoint.Host, endpoint.Port),
            Credential = new MongoCredential(AuthenticationMechanism, persisted.Identity, persisted.Evidence),
            DirectConnection = true,
            RetryReads = false,
            RetryWrites = false,
            ConnectTimeout = NativeTimeout,
            ServerSelectionTimeout = NativeTimeout,
            SocketTimeout = NativeTimeout,
            MaxConnectionPoolSize = MaximumPool,
            ReadPreference = ReadPreference.Nearest
        };
    }
}
