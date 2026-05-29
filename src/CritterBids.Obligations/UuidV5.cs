using System.Security.Cryptography;

namespace CritterBids.Obligations;

/// <summary>
/// RFC 4122 §4.3 UUID v5 generator. Pure function — given the same namespace and name,
/// always produces the same Guid. Used by Obligations to derive a deterministic
/// <c>ObligationId</c> from a <c>ListingId</c> (one obligation per sold listing), so a
/// duplicate <c>SettlementCompleted</c> consumption derives the same id and the saga's
/// idempotency guard rejects re-creation.
///
/// <para><b>BC-internal copy.</b> This mirrors the Settlement BC's <c>UuidV5</c> helper
/// verbatim. The modular-monolith isolation discipline forbids a project reference between
/// BCs, so each BC owns its own copy of this RFC-canonical helper rather than sharing one
/// across the contracts boundary. The byte-swap logic matches Postgres' uuid-ossp and
/// Python's <c>uuid.uuid5</c> for the same inputs.</para>
/// </summary>
internal static class UuidV5
{
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Convert namespace Guid to RFC 4122 big-endian byte form.
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);

        // SHA-1(namespace || name).
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(input);

        // Take the first 16 bytes; set version (5) and variant (RFC 4122) bits.
        Span<byte> result = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(result);

        // Version 5: high nibble of byte 6 is 0101.
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        // Variant RFC 4122: top two bits of byte 8 are 10.
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        // Convert back to .NET Guid byte form (swap big-endian → little-endian for Data1/2/3).
        var output = result.ToArray();
        SwapByteOrder(output);
        return new Guid(output);
    }

    private static void SwapByteOrder(byte[] guidBytes)
    {
        SwapBytes(guidBytes, 0, 3);
        SwapBytes(guidBytes, 1, 2);
        SwapBytes(guidBytes, 4, 5);
        SwapBytes(guidBytes, 6, 7);
    }

    private static void SwapBytes(byte[] bytes, int left, int right)
    {
        (bytes[left], bytes[right]) = (bytes[right], bytes[left]);
    }
}
