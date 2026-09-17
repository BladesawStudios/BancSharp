using ZsDicSharp;

namespace BancSharp;

public sealed class Romfs : IDisposable
{
    private readonly ZsDic _zs;

    private Romfs(string root, ZsDic zs)
    {
        Root = root;
        _zs = zs;
    }

    public string Root { get; }

    public static Romfs Open(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"No romfs at {root}.");

        return new Romfs(root, ZsDic.FromRomfs(root));
    }

    public string? Resolve(string path)
    {
        string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(full)) return full;
        return File.Exists(full + ".zs") ? full + ".zs" : null;
    }

    public bool Exists(string path) => Resolve(path) is not null;

    public byte[] Read(string path)
    {
        string resolved = Resolve(path)
            ?? throw new FileNotFoundException($"No {path} in {Root}, with or without .zs.", path);

        byte[] raw = File.ReadAllBytes(resolved);
        return ZsDic.IsCompressed(raw) ? _zs.Decompress(raw, resolved) : raw;
    }

    public byte[]? TryRead(string path) => Exists(path) ? Read(path) : null;

    public void Dispose() => _zs.Dispose();
}
