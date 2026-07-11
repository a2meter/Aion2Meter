using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Namter.GameData;

public sealed record GameDataManifest(
    ulong DataVersion,
    uint SchemaVersion,
    uint ProtocolProfileVersion,
    Version MinimumAppVersion,
    Uri ArchiveUri,
    long CompressedSize,
    long UncompressedSize,
    string Sha256,
    string Compression,
    DateTimeOffset CreatedUtc,
    string Signature)
{
    public const int MaximumJsonBytes = 64 * 1024;

    public byte[] GetCanonicalUnsignedBytes() => Write(includeSignature: false);

    public byte[] ToJsonBytes() => Write(includeSignature: true);

    public bool Verify(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        try
        {
            byte[] signature = Convert.FromBase64String(Signature);
            if (signature.Length != 64) return false;
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
            return bytesRead == subjectPublicKeyInfo.Length
                && verifier.KeySize == 256
                && verifier.VerifyData(
                    GetCanonicalUnsignedBytes(), signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static GameDataManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > MaximumJsonBytes)
            throw new InvalidDataException("The game-data manifest has an invalid size.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 11)
                throw new InvalidDataException("The game-data manifest must contain exactly the declared properties.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
                if (!seen.Add(property.Name) || !PropertyNames.Contains(property.Name, StringComparer.Ordinal))
                    throw new InvalidDataException("The game-data manifest contains duplicate or unknown properties.");

            var manifest = new GameDataManifest(
                root.GetProperty(nameof(DataVersion)).GetUInt64(),
                root.GetProperty(nameof(SchemaVersion)).GetUInt32(),
                root.GetProperty(nameof(ProtocolProfileVersion)).GetUInt32(),
                Version.Parse(root.GetProperty(nameof(MinimumAppVersion)).GetString()!),
                new Uri(root.GetProperty(nameof(ArchiveUri)).GetString()!, UriKind.Absolute),
                root.GetProperty(nameof(CompressedSize)).GetInt64(),
                root.GetProperty(nameof(UncompressedSize)).GetInt64(),
                root.GetProperty(nameof(Sha256)).GetString()!,
                root.GetProperty(nameof(Compression)).GetString()!,
                root.GetProperty(nameof(CreatedUtc)).GetDateTimeOffset(),
                root.GetProperty(nameof(Signature)).GetString()!);
            manifest.ValidateFields();
            return manifest;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException
            or OverflowException or UriFormatException or ArgumentException)
        {
            throw new InvalidDataException("The game-data manifest is malformed.", exception);
        }
    }

    public void ValidateFields()
    {
        if (DataVersion == 0 || SchemaVersion == 0 || ProtocolProfileVersion == 0)
            throw new InvalidDataException("Manifest versions must be positive.");
        if (MinimumAppVersion is null || ArchiveUri is null || !ArchiveUri.IsAbsoluteUri)
            throw new InvalidDataException("Manifest version and archive URI are required.");
        if (CompressedSize <= 0 || UncompressedSize <= 0)
            throw new InvalidDataException("Manifest sizes must be positive.");
        if (!string.Equals(Compression, "br", StringComparison.Ordinal))
            throw new InvalidDataException("Only Brotli game-data archives are supported.");
        if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Manifest SHA-256 must contain exactly 64 hexadecimal characters.");
        if (string.IsNullOrWhiteSpace(Signature))
            throw new InvalidDataException("Manifest signature is required.");
    }

    private byte[] Write(bool includeSignature)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(DataVersion), DataVersion);
            writer.WriteNumber(nameof(SchemaVersion), SchemaVersion);
            writer.WriteNumber(nameof(ProtocolProfileVersion), ProtocolProfileVersion);
            writer.WriteString(nameof(MinimumAppVersion), MinimumAppVersion.ToString());
            writer.WriteString(nameof(ArchiveUri), ArchiveUri.AbsoluteUri);
            writer.WriteNumber(nameof(CompressedSize), CompressedSize);
            writer.WriteNumber(nameof(UncompressedSize), UncompressedSize);
            writer.WriteString(nameof(Sha256), Sha256);
            writer.WriteString(nameof(Compression), Compression);
            writer.WriteString(nameof(CreatedUtc), CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            if (includeSignature) writer.WriteString(nameof(Signature), Signature);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static readonly string[] PropertyNames =
    [
        nameof(DataVersion), nameof(SchemaVersion), nameof(ProtocolProfileVersion), nameof(MinimumAppVersion),
        nameof(ArchiveUri), nameof(CompressedSize), nameof(UncompressedSize), nameof(Sha256),
        nameof(Compression), nameof(CreatedUtc), nameof(Signature),
    ];
}

public readonly record struct DataVersion(ulong Value);
