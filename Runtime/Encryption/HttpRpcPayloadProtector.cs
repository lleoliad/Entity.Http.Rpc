using System.Security.Cryptography;

namespace Entities.Http.Rpc;

/// <summary>
/// Encrypts and authenticates HTTP RPC bodies using the configured transport-level algorithm.
/// </summary>
internal sealed class HttpRpcPayloadProtector
{
    public const string AlgorithmName = "AesGcm";
    public const int KeySizeInBytes = 32;

    private const byte CurrentVersion = 1;
    private const int NonceSizeInBytes = 12;
    private const int TagSizeInBytes = 16;
    private const int HeaderSizeInBytes = sizeof(byte) + NonceSizeInBytes + TagSizeInBytes;

    private readonly byte[]? _key;

    public HttpRpcPayloadProtector(HttpRpcOptions options)
    {
        if (!options.Encryption.Enabled)
        {
            return;
        }

        if (!TryDecodeKey(options.Encryption.KeyBase64, out var key))
        {
            throw new InvalidOperationException($"Encryption.KeyBase64 must be a valid Base64-encoded {KeySizeInBytes}-byte key.");
        }

        _key = key;
    }

    public bool Enabled => _key is not null;

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (_key is null)
        {
            return plaintext.ToArray();
        }

        var payload = new byte[HeaderSizeInBytes + plaintext.Length];
        payload[0] = CurrentVersion;

        var nonce = payload.AsSpan(sizeof(byte), NonceSizeInBytes);
        var tag = payload.AsSpan(sizeof(byte) + NonceSizeInBytes, TagSizeInBytes);
        var ciphertext = payload.AsSpan(HeaderSizeInBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSizeInBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return payload;
    }

    public bool TryUnprotect(ReadOnlySpan<byte> payload, out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();

        if (_key is null)
        {
            plaintext = payload.ToArray();
            return true;
        }

        if (payload.Length < HeaderSizeInBytes || payload[0] != CurrentVersion)
        {
            return false;
        }

        var nonce = payload.Slice(sizeof(byte), NonceSizeInBytes);
        var tag = payload.Slice(sizeof(byte) + NonceSizeInBytes, TagSizeInBytes);
        var ciphertext = payload.Slice(HeaderSizeInBytes);
        plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSizeInBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            plaintext = Array.Empty<byte>();
            return false;
        }
    }

    internal static bool TryDecodeKey(string? keyBase64, out byte[] key)
    {
        key = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            return false;
        }

        try
        {
            key = Convert.FromBase64String(keyBase64);
            return key.Length == KeySizeInBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
