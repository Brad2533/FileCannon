using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ClipShare.PiServer.Services;

/// <summary>
/// Hashing + generation for account API keys.
///
/// A plain SHA-256 (no salt, no bcrypt/argon2) is deliberate and sufficient here: keys are
/// 256-bit values from a CSPRNG, not user-chosen passwords, so there is nothing to brute-force
/// or rainbow-table — an attacker who can't already produce the raw key can't produce a digest
/// that matches, or one that shares a timing-observable prefix with a stored digest. That last
/// property is also why the auth filter no longer needs a constant-time compare the way the old
/// raw-string check would have: the lookup key is the digest, not the secret itself.
/// </summary>
public static class ApiKeyHasher
{
    private const string KeyPrefix = "fc_";

    /// <summary>Lowercase hex SHA-256 of <paramref name="rawKey"/>. Stable across processes/platforms.</summary>
    public static string Hash(string rawKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// A fresh key: <c>fc_</c> + base64url of 32 random bytes. base64url so it drops straight
    /// into an <c>X-Api-Key</c> header and a settings.json string with no escaping.
    /// </summary>
    public static string GenerateRawKey() => KeyPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
