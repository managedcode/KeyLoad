using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresVectorStorage
{
    private const string JsonStringType = "string";
    private const string JsonNumberType = "number";
    private const string FloatFormat = "R";
    private const string AddReadbackColumnsSql = "ALTER TABLE documents ADD COLUMN vector_number integer; CREATE INDEX vectors_number_idx ON documents(vector_number)";
    private const string InsertSql = "INSERT INTO documents(id,body,embedding,vector_number) SELECT u.id,u.payload::jsonb,u.embedding::vector,u.number FROM unnest($1::text[],$2::text[],$3::text[],$4::integer[]) AS u(id,payload,embedding,number)";
    private const string ReadbackSql = "SELECT vector_number,id,embedding::text,body->>'id',body->>'number',body->>'padding',(SELECT count(*) FROM jsonb_object_keys(body)),jsonb_typeof(body->'id'),jsonb_typeof(body->'number'),jsonb_typeof(body->'padding') FROM documents WHERE vector_number > $1 ORDER BY vector_number LIMIT $2";
    private const string ReadOneSql = "SELECT vector_number,id,embedding::text,body->>'id',body->>'number',body->>'padding',(SELECT count(*) FROM jsonb_object_keys(body)),jsonb_typeof(body->'id'),jsonb_typeof(body->'number'),jsonb_typeof(body->'padding') FROM documents WHERE id=$1";
    private const string UpdateSql = "UPDATE documents SET embedding=$2::vector WHERE id=$1";

    internal static async Task AddVectorReadbackColumnsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = AddReadbackColumnsSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> IngestAsync(NpgsqlDataSource source, IAsyncEnumerable<VectorDocument> documents, ComparisonExecutionOptions execution,
        int vectorComponentBuilderCapacity, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var batch = new List<VectorDocument>(execution.WriteBatchCapacity);
        var inserted = PostgresVectorStorageValues.FirstIndex;
        await foreach (var document in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(document);
            if (batch.Count == execution.WriteBatchCapacity)
            {
                inserted += await InsertBatchAsync(connection, transaction, batch, vectorComponentBuilderCapacity, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
        if (batch.Count > PostgresVectorStorageValues.FirstIndex)
        {
            inserted += await InsertBatchAsync(connection, transaction, batch, vectorComponentBuilderCapacity, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    internal static async IAsyncEnumerable<VectorReadback> ReadbackAsync(NpgsqlDataSource source,
        ComparisonExecutionOptions execution, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var last = -PostgresVectorStorageValues.SingleElementOffset;
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ReadbackSql;
            command.Parameters.AddWithValue(last);
            command.Parameters.AddWithValue(execution.ReadbackBatchCapacity);
            var count = PostgresVectorStorageValues.FirstIndex;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actual = ToReadback(reader);
                if (actual.Number <= last)
                {
                    throw new InvalidDataException(PostgresVectorStorageValues.PostgreSQLJSONBPayloadFieldsDifferFrom);
                }
                last = actual.Number;
                count++;
                yield return actual;
            }
            if (count < execution.ReadbackBatchCapacity)
            {
                yield break;
            }
        }
    }

    internal static async Task UpdateAsync(NpgsqlDataSource source, VectorUpdate update, int vectorComponentBuilderCapacity, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpdateSql;
        command.Parameters.AddWithValue(update.Id);
        command.Parameters.AddWithValue(VectorLiteral(update.Embedding.Span, vectorComponentBuilderCapacity));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != PostgresVectorStorageValues.SingleElementOffset)
        {
            throw new InvalidDataException(PostgresVectorStorageValues.PostgreSQLDidNotUpdateExactlyOne);
        }
    }

    internal static async Task<global::ManagedCode.Communication.Result<VectorReadback?>> ReadAsync(NpgsqlDataSource source, string id, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ReadOneSql;
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? global::ManagedCode.Communication.Result<VectorReadback?>.Succeed(((VectorReadback?)(ToReadback(reader)))) : global::ManagedCode.Communication.Result<VectorReadback?>.FailNotFound(nameof(ReadAsync));
    }

    internal static string VectorLiteral(ReadOnlySpan<float> vector, int vectorComponentBuilderCapacity)
    {
        var builder = CreateVectorBuilder(vector.Length, vectorComponentBuilderCapacity).Append(PostgresVectorStorageValues.VectorOpenCharacter);
        for (var index = PostgresVectorStorageValues.FirstIndex; index < vector.Length; index++)
        {
            if (index != PostgresVectorStorageValues.FirstIndex)
            {
                builder.Append(PostgresVectorStorageValues.ItemSeparatorCharacter);
            }

            builder.Append(vector[index].ToString(FloatFormat, CultureInfo.InvariantCulture));
        }
        return builder.Append(PostgresVectorStorageValues.VectorCloseCharacter).ToString();
    }

    internal static StringBuilder CreateVectorBuilder(int componentCount, int vectorComponentBuilderCapacity)
        => new(componentCount * vectorComponentBuilderCapacity + PostgresVectorStorageValues.VectorDelimiterCharacterCount);

    private static async Task<int> InsertBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        List<VectorDocument> batch, int vectorComponentBuilderCapacity, CancellationToken cancellationToken)
    {
        var ids = new string[batch.Count];
        var payloads = new string[batch.Count];
        var vectors = new string[batch.Count];
        var numbers = new int[batch.Count];
        for (var index = PostgresVectorStorageValues.FirstIndex; index < batch.Count; index++)
        {
            var document = batch[index];
            if (document.Embedding.Length != PostgresVectorStorageValues.VectorDimensions || Encoding.UTF8.GetByteCount(document.Payload) != PostgresVectorStorageValues.PayloadBytes)
            {
                throw new InvalidDataException(PostgresVectorStorageValues.TheVectorDocumentViolatesItsFrozen);
            }

            ids[index] = document.Id;
            payloads[index] = document.Payload;
            vectors[index] = VectorLiteral(document.Embedding.Span, vectorComponentBuilderCapacity);
            numbers[index] = document.Number;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql;
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, ids);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, payloads);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, vectors);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Integer, numbers);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static VectorReadback ToReadback(NpgsqlDataReader reader)
    {
        var number = reader.GetInt32(PostgresVectorStorageValues.NumberColumnOrdinal);
        var id = reader.GetString(PostgresVectorStorageValues.IdColumnOrdinal);
        var vector = ParseVector(reader.GetString(PostgresVectorStorageValues.EmbeddingColumnOrdinal));
        var bodyId = reader.GetString(PostgresVectorStorageValues.PayloadIdColumnOrdinal);
        var bodyNumber = reader.GetString(PostgresVectorStorageValues.PayloadNumberColumnOrdinal);
        var padding = reader.GetString(PostgresVectorStorageValues.PaddingColumnOrdinal);
        if (bodyId != id || !int.TryParse(bodyNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedNumber)
            || parsedNumber != number || reader.GetInt64(PostgresVectorStorageValues.FieldCountColumnOrdinal) != PostgresVectorStorageValues.PayloadPropertyCount
            || reader.GetString(PostgresVectorStorageValues.IdTypeColumnOrdinal) != JsonStringType || reader.GetString(PostgresVectorStorageValues.NumberTypeColumnOrdinal) != JsonNumberType
            || reader.GetString(PostgresVectorStorageValues.PaddingTypeColumnOrdinal) != JsonStringType)
        {
            throw new InvalidDataException(PostgresVectorStorageValues.PostgreSQLJSONBPayloadFieldsDifferFrom);
        }

        var payload = CanonicalPayload(bodyId, parsedNumber, padding);
        return new(number, id, vector.Length, VectorComparisonCorpus.HashVector(vector), HashUtf8(payload));
    }

    internal static string CanonicalPayload(string id, int number, string padding)
    {
        if (id.Length != PostgresVectorStorageValues.IdCharacterCount || !id.StartsWith(PostgresVectorStorageValues.VectorIdPrefixCharacter) || padding.Length != PostgresVectorStorageValues.PayloadBytes - Encoding.ASCII.GetByteCount($"{PostgresVectorStorageValues.CanonicalPayloadPrefix}{id}{PostgresVectorStorageValues.CanonicalNumberPrefix}{number}{PostgresVectorStorageValues.CanonicalEmptyPaddingSuffix}"))
        {
            throw new InvalidDataException(PostgresVectorStorageValues.PostgreSQLJSONBPayloadDoesNotMatch);
        }

        return $"{PostgresVectorStorageValues.CanonicalPayloadPrefix}{id}{PostgresVectorStorageValues.CanonicalNumberPrefix}{number}{PostgresVectorStorageValues.CanonicalPaddingPrefix}{padding}{PostgresVectorStorageValues.CanonicalPayloadSuffix}";
    }

    private static float[] ParseVector(string serialized)
    {
        if (serialized.Length < PostgresVectorStorageValues.VectorDelimiterCharacterCount || serialized[PostgresVectorStorageValues.FirstIndex] != PostgresVectorStorageValues.VectorOpenCharacter || serialized[^PostgresVectorStorageValues.SingleElementOffset] != PostgresVectorStorageValues.VectorCloseCharacter)
        {
            throw new InvalidDataException(PostgresVectorStorageValues.PostgreSQLReturnedAnInvalidPgvectorValue);
        }

        return serialized.AsSpan(PostgresVectorStorageValues.SingleElementOffset, serialized.Length - PostgresVectorStorageValues.VectorDelimiterCharacterCount).ToString().Split(PostgresVectorStorageValues.ItemSeparatorCharacter)
            .Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
    }

    private static string HashUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
