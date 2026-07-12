using System.Text.Json;
using Namter.Core.Interop;

namespace Namter.Cli;

public sealed record CliConfiguration(
    string DataDirectory, Uri? GameDataManifestUri, string? GameDataPublicKeySpki,
    uint NativeQueueCapacity, int ManagedQueueCapacity, uint MaxLiveFlows,
    uint MaxOutOfOrderBytesPerFlow, uint MaxFrameBytes, uint MaxDecompressedBytes)
{
    public static CliConfiguration Load(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Settings value = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(path), JsonOptions) ?? new() : new();
        string? uriText = environment("NAMTER_GAMEDATA_MANIFEST_URI") ?? value.GameDataManifestUri;
        string? publicKey = environment("NAMTER_GAMEDATA_PUBLIC_KEY_SPKI") ?? value.GameDataPublicKeySpki;
        Uri? uri = string.IsNullOrWhiteSpace(uriText) ? null : new Uri(uriText, UriKind.Absolute);
        return new CliConfiguration(value.DataDirectory ?? "data", uri, publicKey, value.NativeQueueCapacity, value.ManagedQueueCapacity,
            value.MaxLiveFlows, value.MaxOutOfOrderBytesPerFlow, value.MaxFrameBytes, value.MaxDecompressedBytes).Validate();
    }
    public bool RemoteConfigured => GameDataManifestUri is not null && !string.IsNullOrWhiteSpace(GameDataPublicKeySpki);
    public string RemoteStatus => RemoteConfigured ? "Configured" : "NotConfigured";
    private CliConfiguration Validate()
    {
        if (ManagedQueueCapacity is < 16 or > 1_048_576) throw new InvalidDataException("ManagedQueueCapacity is outside 16..1048576.");
        _ = new NativeCoreConfig(NativeQueueCapacity, MaxLiveFlows, MaxOutOfOrderBytesPerFlow, MaxFrameBytes, MaxDecompressedBytes);
        if (GameDataManifestUri is not null && GameDataManifestUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("GameDataManifestUri must use HTTPS.");
        return this;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private sealed record Settings
    {
        public string? DataDirectory { get; init; } = "data"; public string? GameDataManifestUri { get; init; } public string? GameDataPublicKeySpki { get; init; }
        public uint NativeQueueCapacity { get; init; } = 1024; public int ManagedQueueCapacity { get; init; } = 1024; public uint MaxLiveFlows { get; init; } = 512;
        public uint MaxOutOfOrderBytesPerFlow { get; init; } = 1_048_576; public uint MaxFrameBytes { get; init; } = 1_048_576; public uint MaxDecompressedBytes { get; init; } = 4_194_304;
    }
}
