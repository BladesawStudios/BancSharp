using ZsDicSharp;

namespace BancSharp;

/// <summary>
/// A romfs on disk, with the decompression its files need.
/// </summary>
/// <remarks>
/// Nearly everything shipped is zstd against one of the game's dictionaries, and paths are
/// written down inside the data without the <c>.zs</c> the file on disk actually carries -
/// a merged batch names <c>Banc/.../Mrg_….bcett.byml</c> and the file is
/// <c>Mrg_….bcett.byml.zs</c>. Reading a path therefore means trying both, which is worth
/// doing in one place.
/// </remarks>
public sealed class Romfs : IDisposable
{
    private readonly ZsDic _zs;

    private Romfs(string root, ZsDic zs)
    {
        Root = root;
        _zs = zs;
    }

    public string Root { get; }

    /// <exception cref="DirectoryNotFoundException">There is no romfs there.</exception>
    public static Romfs Open(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"No romfs at {root}.");

        return new Romfs(root, ZsDic.FromRomfs(root));
    }

    /// <summary>The path on disk for a romfs-relative path, with or without its <c>.zs</c>.</summary>
    public string? Resolve(string path)
    {
        string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(full)) return full;
        return File.Exists(full + ".zs") ? full + ".zs" : null;
    }

    public bool Exists(string path) => Resolve(path) is not null;

    /// <summary>Reads a file, decompressed if it needed to be.</summary>
    /// <exception cref="FileNotFoundException">The file is not there under either name.</exception>
    public byte[] Read(string path)
    {
        string resolved = Resolve(path)
            ?? throw new FileNotFoundException($"No {path} in {Root}, with or without .zs.", path);

        byte[] raw = File.ReadAllBytes(resolved);
        return ZsDic.IsCompressed(raw) ? _zs.Decompress(raw, resolved) : raw;
    }

    /// <summary>Reads a file, or null when it is not there.</summary>
    public byte[]? TryRead(string path) => Exists(path) ? Read(path) : null;

    public void Dispose() => _zs.Dispose();
}
