using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;

namespace Namter.GameData;

internal static class P256Signature
{
    internal const string CurveOid = "1.2.840.10045.3.1.7";
    private static readonly BigInteger Order = BigInteger.Parse(
        "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        NumberStyles.HexNumber,
        CultureInfo.InvariantCulture);
    private static readonly BigInteger HalfOrder = Order >> 1;

    public static bool IsExactCurve(ECDsa algorithm)
    {
        try
        {
            ECParameters parameters = algorithm.ExportParameters(includePrivateParameters: false);
            return string.Equals(parameters.Curve.Oid.Value, CurveOid, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static bool IsCanonical(ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64) return false;
        BigInteger r = new(signature[..32], isUnsigned: true, isBigEndian: true);
        BigInteger s = new(signature[32..], isUnsigned: true, isBigEndian: true);
        return r > BigInteger.Zero && r < Order && s > BigInteger.Zero && s <= HalfOrder;
    }

    public static byte[] Normalize(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != 64) throw new CryptographicException("P-256 signatures must contain 64 bytes.");
        BigInteger s = new(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        if (s <= BigInteger.Zero || s >= Order) throw new CryptographicException("P-256 signature scalar is out of range.");
        if (s <= HalfOrder) return signature;
        Span<byte> scalar = signature.AsSpan(32);
        scalar.Clear();
        if (!(Order - s).TryWriteBytes(scalar, out _, isUnsigned: true, isBigEndian: true))
            throw new CryptographicException("Could not normalize P-256 signature scalar.");
        return signature;
    }
}
