using System.Security.Cryptography;
using System.Text;

namespace ClashDetection.Api.Hashing;

/// <summary>
/// Produces a stable content hash of a request body, used as the cache key so that two requests
/// with identical input share a single computation/result. Hashing the raw bytes keeps this O(n)
/// and avoids a re-serialization round-trip; callers that need canonicalization (whitespace /
/// key-order insensitivity) can normalize before calling.
/// </summary>
public static class InputHasher
{
    public static string Hash(ReadOnlySpan<byte> utf8Body)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(utf8Body, digest);
        return Convert.ToHexStringLower(digest);
    }

    public static string Hash(string body) => Hash(Encoding.UTF8.GetBytes(body));
}
