using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Infrastructure.Security;

namespace Wms.Infrastructure.Marketplaces;

/// <summary>AES-GCM; ключ из Marketplace:ProtectionKey или Jwt:SigningKey. Формат: enc1.&lt;base64(nonce|tag|cipher)&gt;.</summary>
public sealed class AesSecretProtector : ISecretProtector
{
    public const string Prefix = "enc1.";
    private readonly byte[] _key;

    public AesSecretProtector(IOptions<MarketplaceOptions> marketplace, IOptions<JwtOptions> jwt)
    {
        var secret = marketplace.Value.ProtectionKey;
        if (string.IsNullOrWhiteSpace(secret)) secret = jwt.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Задайте Marketplace:ProtectionKey или Jwt:SigningKey для шифрования ключей API.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var packed = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, packed, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(packed);
    }

    public string? TryUnprotect(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            return null;
        try
        {
            var packed = Convert.FromBase64String(ciphertext[Prefix.Length..]);
            if (packed.Length < 28) return null;
            var nonce = packed.AsSpan(0, 12);
            var tag = packed.AsSpan(12, 16);
            var cipher = packed.AsSpan(28);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
