using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class PostgresRf3Caller : IAsyncDisposable
{
    private readonly NpgsqlDataSource source;
    internal NpgsqlConnection Connection
    {
        get;
    }

    private PostgresRf3Caller(NpgsqlDataSource source, NpgsqlConnection connection)
    {
        this.source = source;
        Connection = connection;
    }

    internal static async Task<PostgresRf3Caller> ConnectAsync(DistributedApplication app, string node,
        string subject, string credential, string rootCertificate, CancellationToken token,
        SslMode sslMode = SslMode.VerifyFull)
    {
        var endpoint = app.GetEndpoint(node, PostgresRf3Protocol.Endpoint);
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            Database = PostgresRf3Protocol.DatabaseAlias,
            Username = subject,
            Password = credential,
            SslMode = sslMode,
            RootCertificate = rootCertificate,
            GssEncryptionMode = GssEncryptionMode.Disable,
            Pooling = false,
            ApplicationName = PostgresRf3Protocol.Application
        };
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        builder.ConfigureTypeLoading(options => options.EnableTypeLoading(false));
        builder.EnableParameterLogging(false);
        var source = builder.Build();
        try
        {
            return new(source, await source.OpenConnectionAsync(token).ConfigureAwait(false));
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => source.DisposeAsync().AsTask(), failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task<T> ExecuteAsync<T>(string sql, IReadOnlyList<NpgsqlParameter> parameters,
        bool prepared, CancellationToken token)
    {
        T? result = default;
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var command = PostgresRf3Commands.Create(sql, Connection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                foreach (var parameter in parameters)
                {
                    command.Parameters.Add(parameter);
                }
                if (prepared)
                {
                    await command.PrepareAsync(token).ConfigureAwait(false);
                }
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    result = await ReadAsync<T>(reader, token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private static async Task<T> ReadAsync<T>(NpgsqlDataReader reader, CancellationToken token)
    {
        await global::TUnit.Assertions.Assert.That(reader.FieldCount).IsEqualTo(PostgresRf3Protocol.OneColumn);
        await global::TUnit.Assertions.Assert.That(reader.GetName(PostgresRf3Protocol.FirstColumn)).IsEqualTo(PostgresRf3Protocol.ResultColumn);
        await global::TUnit.Assertions.Assert.That(reader.GetDataTypeName(PostgresRf3Protocol.FirstColumn)).IsEqualTo(PostgresRf3Protocol.JsonbType);
        await global::TUnit.Assertions.Assert.That(await reader.ReadAsync(token).ConfigureAwait(false)).IsTrue();
        var exact = reader.GetString(PostgresRf3Protocol.FirstColumn);
        var result = JsonSerializer.Deserialize<T>(exact, JsonDefaults.Options)
            ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        await global::TUnit.Assertions.Assert.That(await reader.ReadAsync(token).ConfigureAwait(false)).IsFalse();
        await global::TUnit.Assertions.Assert.That(await reader.NextResultAsync(token).ConfigureAwait(false)).IsFalse();
        return result;
    }

    internal Task<CommitReceipt> CommitAsync(CommandRequest command, bool prepared, CancellationToken token,
        NpgsqlDbType parameterType = NpgsqlDbType.Jsonb)
    {
        var envelope = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [PostgresRf3Protocol.Request] = JsonSerializer.SerializeToElement(command, JsonDefaults.Options)
        };
        var argument = new NpgsqlParameter
        {
            NpgsqlDbType = parameterType,
            Value = JsonSerializer.Serialize(envelope, JsonDefaults.Options)
        };
        return ExecuteAsync<CommitReceipt>(PostgresRf3Protocol.CommitSql, [argument], prepared, token);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Connection.DisposeAsync().AsTask(), failures);
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => source.DisposeAsync().AsTask(), failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
