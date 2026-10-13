using System.Buffers.Binary;
using System.Text;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3WireFrames
{
    internal static byte[] SslRequest()
    {
        var bytes = new byte[LengthBytes + LengthBytes];
        BinaryPrimitives.WriteInt32BigEndian(bytes, bytes.Length);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(LengthBytes), SslCode);
        return bytes;
    }

    internal static byte[] Startup(string subject)
    {
        var fields = Encoding.UTF8.GetBytes(User + Null + subject + Null + Database + Null
            + PostgresRf3Protocol.DatabaseAlias + Null + Null);
        var bytes = new byte[LengthBytes + LengthBytes + fields.Length];
        BinaryPrimitives.WriteInt32BigEndian(bytes, bytes.Length);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(LengthBytes), Version);
        fields.CopyTo(bytes, LengthBytes + LengthBytes);
        return bytes;
    }

    internal static byte[] StalledStartup()
    {
        var bytes = new byte[LengthBytes + LengthBytes];
        BinaryPrimitives.WriteInt32BigEndian(bytes, bytes.Length + LengthBytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(LengthBytes), Version);
        return bytes;
    }

    internal static byte[] StalledPassword()
    {
        var bytes = new byte[sizeof(byte) + LengthBytes];
        bytes[PostgresRf3Protocol.FirstColumn] = Password;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(sizeof(byte)), LengthBytes + LengthBytes);
        return bytes;
    }

    internal static async Task PasswordChallengeAsync(Stream stream, CancellationToken token)
    {
        var bytes = new byte[sizeof(byte) + LengthBytes + LengthBytes];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        await global::TUnit.Assertions.Assert.That(bytes[PostgresRf3Protocol.FirstColumn]).IsEqualTo(Authentication);
        await global::TUnit.Assertions.Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(sizeof(byte)))).IsEqualTo(LengthBytes + LengthBytes);
        await global::TUnit.Assertions.Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(sizeof(byte) + LengthBytes))).IsEqualTo(PasswordCode);
    }

    internal const byte SslAccepted = (byte)'S';
    private const byte Password = (byte)'p';
    private const byte Authentication = (byte)'R';
    private const string User = "user";
    private const string Database = "database";
    private const string Null = "\0";
    private const int LengthBytes = sizeof(int);
    private const int SslCode = 80877103;
    private const int Version = 196608;
    private const int PasswordCode = 3;
}
