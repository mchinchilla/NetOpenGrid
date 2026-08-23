using System.Security.Cryptography;

namespace NetOpenGrid.Infrastructure.Assets;

public sealed record EmbeddedAsset(byte[] Bytes, string Version, string ContentType);

/// <summary>
/// Client runtime and vendor libraries embedded in the assembly, loaded once per
/// process. The version (short SHA-256) enables immutable caching via ?v= URLs.
/// </summary>
public static class EmbeddedGridAssets
{
    public static readonly EmbeddedAsset ClientRuntime = Load("netopengrid.js");
    public static readonly EmbeddedAsset Htmx = Load("vendor/htmx.min.js");
    public static readonly EmbeddedAsset Alpine = Load("vendor/alpine.min.js");

    private static EmbeddedAsset Load(string relativePath)
    {
        var assembly = typeof(EmbeddedGridAssets).Assembly;
        var logicalName = $"NetOpenGrid.Infrastructure.Assets.{relativePath.Replace('/', '.')}";
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded asset '{relativePath}' ('{logicalName}') is missing from the assembly.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var version = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();

        return new EmbeddedAsset(bytes, version, "text/javascript; charset=utf-8");
    }
}
