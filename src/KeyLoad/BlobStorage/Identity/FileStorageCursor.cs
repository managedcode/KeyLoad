using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class FileStorageCursor(IOptions<NodeOptions> options)
{
    private const string KeyDomain = "KeyLoad.FileStorageCursor.v1";
    private const string Invalid = "The file cursor is invalid for the current resource and policy.";
    private const char FrameSeparator = '\0';
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int MaximumCursorCharacters = 8192;
    private const int FirstByte = 0;

    internal global::ManagedCode.Communication.Result<string?> Encode(FileStorageRequest request, string? after)
    {
        if (after is null)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(Encode));
        }
        var key = Key();
        try
        {
            using var cipher = new AesGcm(key, TagBytes);
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var bytes = Encoding.UTF8.GetBytes(after);
            var output = new byte[bytes.Length];
            var tag = new byte[TagBytes];
            cipher.Encrypt(nonce, bytes, output, tag, Context(request));
            return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(WebEncoders.Base64UrlEncode([.. nonce, .. tag, .. output]))));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal global::ManagedCode.Communication.Result<string?> Decode(FileStorageRequest request, string? cursor)
    {
        if (cursor is null)
        {
            return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(Decode));
        }
        if (cursor.Length > MaximumCursorCharacters)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        var key = Key();
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            if (bytes.Length < NonceBytes + TagBytes)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
            }
            using var cipher = new AesGcm(key, TagBytes);
            var output = new byte[bytes.Length - NonceBytes - TagBytes];
            cipher.Decrypt(bytes.AsSpan(FirstByte, NonceBytes), bytes.AsSpan(NonceBytes + TagBytes),
                bytes.AsSpan(NonceBytes, TagBytes), output, Context(request));
            return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(Encoding.UTF8.GetString(output))));
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] Key()
    {
        var master = Convert.FromBase64String(options.Value.SigningKey);
        try
        {
            return HMACSHA256.HashData(master, Encoding.UTF8.GetBytes(KeyDomain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }

    private static byte[] Context(FileStorageRequest request)
        => Encoding.UTF8.GetBytes(string.Join(FrameSeparator, request.Scope.PhysicalNamespace,
            request.PrincipalId, request.PolicyEpoch.ToString(CultureInfo.InvariantCulture)));
}
