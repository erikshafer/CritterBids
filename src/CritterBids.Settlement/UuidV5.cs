using System.Security.Cryptography;

namespace CritterBids.Settlement;

/// <summary>
/// RFC 4122 §4.3 UUID v5 generator. Pure function — given the same namespace and name,
/// always produces the same Guid. Used by Settlement to derive a deterministic
/// <c>SettlementId</c> from a <c>ListingId</c> per W003 Phase 1 Part 6, so a duplicate
/// <c>ListingSold</c> consumption derives the same id and the saga's idempotency guard
/// rejects re-creation.
///
/// <para><b>RFC 4122 byte order quirk.</b> .NET's <see cref="Guid"/> stores Data1 / Data2 / Data3
/// little-endian internally; RFC 4122 specifies big-endian for the SHA-1 input. The helper
/// performs the byte swap on input (namespace bytes) and on output (the resulting Guid)
/// so the produced value matches what other RFC 4122 implementations (Postgres' uuid-ossp
/// extension, Python's <c>uuid.uuid5</c>, etc.) would produce for the same inputs.</para>
///
/// <para>First lived UUID v5 use in CritterBids — M5-S4 authors this helper. M4-S1 pinned
/// the namespace-constant pattern in <c>AuctionsIdentityNamespaces</c>; that BC's first
/// consumer (M4-S3 Proxy Bid Manager saga) has not shipped, so M5-S4 is the helper's
/// authoring slice. The shape is RFC-canonical and reusable — Auctions can adopt it
/// when M4-S3 lands.</para>
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
