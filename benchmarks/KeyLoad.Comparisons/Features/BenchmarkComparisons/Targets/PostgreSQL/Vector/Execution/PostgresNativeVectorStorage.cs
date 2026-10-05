using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresNativeVectorStorage
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

    internal static async Task<int> IngestAsync(NpgsqlDataSource source, IAsyncEnumerable<VectorDocument> documents, NativeComparisonExecutionOptions execution,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var batch = new List<VectorDocument>(execution.WriteBatchCapacity);
        var inserted = PostgresNativeVectorStorageValues.FirstIndex;
        await foreach (var document in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(document);
            if (batch.Count == execution.WriteBatchCapacity)
            {
                inserted += await InsertBatchAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
        if (batch.Count > PostgresNativeVectorStorageValues.FirstIndex)
        {
            inserted += await InsertBatchAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    internal static async IAsyncEnumerable<VectorReadback> ReadbackAsync(NpgsqlDataSource source,
        NativeComparisonExecutionOptions execution, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var last = -PostgresNativeVectorStorageValues.SingleElementOffset;
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ReadbackSql;
            command.Parameters.AddWithValue(last);
            command.Parameters.AddWithValue(execution.ReadbackBatchCapacity);
            var count = PostgresNativeVectorStorageValues.FirstIndex;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actual = ToReadback(reader);
                if (actual.Number <= last)
                {
                    throw new InvalidDataException(PostgresNativeVectorStorageValues.PostgreSQLJSONBPayloadFieldsDifferFrom);
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

    internal static async Task UpdateAsync(NpgsqlDataSource source, VectorUpdate update, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpdateSql;
        command.Parameters.AddWithValue(update.Id);
        command.Parameters.AddWithValue(VectorLiteral(update.Embedding.Span));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != PostgresNativeVectorStorageValues.SingleElementOffset)
        {
            throw new InvalidDataException(PostgresNativeVectorStorageValues.PostgreSQLDidNotUpdateExactlyOne);
        }
    }

    internal static async Task<VectorReadback?> ReadAsync(NpgsqlDataSource source, string id, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ReadOneSql;
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ToReadback(reader) : null;
    }

    internal static string VectorLiteral(ReadOnlySpan<float> vector)
    {
        var builder = new StringBuilder(vector.Length * PostgresNativeVectorStorageValues.FloatTextCapacityEstimate + PostgresNativeVectorStorageValues.VectorDelimiterCharacterCount).Append(PostgresNativeVectorStorageValues.VectorOpenCharacter);
        for (var index = PostgresNativeVectorStorageValues.FirstIndex; index < vector.Length; index++)
        {
            if (index != PostgresNativeVectorStorageValues.FirstIndex)
            {
                builder.Append(PostgresNativeVectorStorageValues.ItemSeparatorCharacter);
            }

            builder.Append(vector[index].ToString(FloatFormat, CultureInfo.InvariantCulture));
        }
        return builder.Append(PostgresNativeVectorStorageValues.VectorCloseCharacter).ToString();
    }

    private static async Task<int> InsertBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        List<VectorDocument> batch, CancellationToken cancellationToken)
    {
        var ids = new string[batch.Count];
        var payloads = new string[batch.Count];
        var vectors = new string[batch.Count];
        var numbers = new int[batch.Count];
        for (var index = PostgresNativeVectorStorageValues.FirstIndex; index < batch.Count; index++)
        {
            var document = batch[index];
            if (document.Embedding.Length != PostgresNativeVectorStorageValues.VectorDimensions || Encoding.UTF8.GetByteCount(document.Payload) != PostgresNativeVectorStorageValues.PayloadBytes)
            {
                throw new InvalidDataException(PostgresNativeVectorStorageValues.TheVectorDocumentViolatesItsFrozen);
            }

            ids[index] = document.Id;
            payloads[index] = document.Payload;
            vectors[index] = VectorLiteral(document.Embedding.Span);
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
        var number = reader.GetInt32(PostgresNativeVectorStorageValues.NumberColumnOrdinal);
        var id = reader.GetString(PostgresNativeVectorStorageValues.IdColumnOrdinal);
        var vector = ParseVector(reader.GetString(PostgresNativeVectorStorageValues.EmbeddingColumnOrdinal));
        var bodyId = reader.GetString(PostgresNativeVectorStorageValues.PayloadIdColumnOrdinal);
        var bodyNumber = reader.GetString(PostgresNativeVectorStorageValues.PayloadNumberColumnOrdinal);
        var padding = reader.GetString(PostgresNativeVectorStorageValues.PaddingColumnOrdinal);
        if (bodyId != id || !int.TryParse(bodyNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedNumber)
            || parsedNumber != number || reader.GetInt64(PostgresNativeVectorStorageValues.FieldCountColumnOrdinal) != PostgresNativeVectorStorageValues.PayloadPropertyCount
            || reader.GetString(PostgresNativeVectorStorageValues.IdTypeColumnOrdinal) != JsonStringType || reader.GetString(PostgresNativeVectorStorageValues.NumberTypeColumnOrdinal) != JsonNumberType
            || reader.GetString(PostgresNativeVectorStorageValues.PaddingTypeColumnOrdinal) != JsonStringType)
        {
            throw new InvalidDataException(PostgresNativeVectorStorageValues.PostgreSQLJSONBPayloadFieldsDifferFrom);
        }

        var payload = CanonicalPayload(bodyId, parsedNumber, padding);
        return new(number, id, vector.Length, VectorComparisonCorpus.HashVector(vector), HashUtf8(payload));
    }

    internal static string CanonicalPayload(string id, int number, string padding)
    {
        if (id.Length != PostgresNativeVectorStorageValues.IdCharacterCount || !id.StartsWith(PostgresNativeVectorStorageValues.VectorIdPrefixCharacter) || padding.Length != PostgresNativeVectorStorageValues.PayloadBytes - Encoding.ASCII.GetByteCount($"{PostgresNativeVectorStorageValues.CanonicalPayloadPrefix}{id}{PostgresNativeVectorStorageValues.CanonicalNumberPrefix}{number}{PostgresNativeVectorStorageValues.CanonicalEmptyPaddingSuffix}"))
        {
            throw new InvalidDataException(PostgresNativeVectorStorageValues.PostgreSQLJSONBPayloadDoesNotMatch);
        }

        return $"{PostgresNativeVectorStorageValues.CanonicalPayloadPrefix}{id}{PostgresNativeVectorStorageValues.CanonicalNumberPrefix}{number}{PostgresNativeVectorStorageValues.CanonicalPaddingPrefix}{padding}{PostgresNativeVectorStorageValues.CanonicalPayloadSuffix}";
    }

    private static float[] ParseVector(string serialized)
    {
        if (serialized.Length < PostgresNativeVectorStorageValues.VectorDelimiterCharacterCount || serialized[PostgresNativeVectorStorageValues.FirstIndex] != PostgresNativeVectorStorageValues.VectorOpenCharacter || serialized[^PostgresNativeVectorStorageValues.SingleElementOffset] != PostgresNativeVectorStorageValues.VectorCloseCharacter)
        {
            throw new InvalidDataException(PostgresNativeVectorStorageValues.PostgreSQLReturnedAnInvalidPgvectorValue);
        }

        return serialized.AsSpan(PostgresNativeVectorStorageValues.SingleElementOffset, serialized.Length - PostgresNativeVectorStorageValues.VectorDelimiterCharacterCount).ToString().Split(PostgresNativeVectorStorageValues.ItemSeparatorCharacter)
            .Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
    }

    private static string HashUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
