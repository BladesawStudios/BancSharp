using ZsDicSharp;

namespace BancSharp;

public sealed class Romfs : IDisposable
{
    private readonly ZsDic _zs;

    private Romfs(string root, string? overlay, ZsDic zs)
    {
        Root = root;
        Overlay = overlay;
        _zs = zs;
    }

    public string Root { get; }

    /// <summary>
    /// A second romfs tree laid over <see cref="Root"/> - a mod's - whose files win over the
    /// dump's wherever it has them. Null reads the dump alone.
    /// </summary>
    public string? Overlay { get; }

    /// <param name="overlay">A mod's romfs folder to read through, or null.</param>
    public static Romfs Open(string root, string? overlay = null)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"No romfs at {root}.");

        if (overlay is not null && !Directory.Exists(overlay)) overlay = null;
        return new Romfs(root, overlay, ZsDic.FromRomfs(root));
    }

    public string? Resolve(string path)
    {
        string rel = path.Replace('/', Path.DirectorySeparatorChar);

        if (Overlay is not null)
        {
            string mod = Path.Combine(Overlay, rel);
            if (File.Exists(mod)) return mod;
            if (File.Exists(mod + ".zs")) return mod + ".zs";
        }

        string full = Path.Combine(Root, rel);
        if (File.Exists(full)) return full;
        return File.Exists(full + ".zs") ? full + ".zs" : null;
    }

    public bool Exists(string path) => Resolve(path) is not null;

    /// <summary>
    /// The file to read for a path on disk under <see cref="Root"/>: the overlay's copy when it
    /// has one, else the path as given. Paths outside the romfs come back unchanged.
    /// </summary>
    public string MapPath(string fullPath)
    {
        if (Overlay is null) return fullPath;

        string rel = Path.GetRelativePath(Root, fullPath);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return fullPath;

        string mod = Path.Combine(Overlay, rel);
        return File.Exists(mod) ? mod : fullPath;
    }

    /// <summary>
    /// The files in a romfs folder matching a pattern, from the dump and the overlay together,
    /// as romfs paths with forward slashes and any <c>.zs</c> taken off - what
    /// <see cref="Read"/> takes. Sorted, and each named once.
    /// </summary>
    /// <param name="pattern">A file name pattern without the <c>.zs</c>, e.g. <c>*_Static.bcett.byml</c>.</param>
    public List<string> List(string folder, string pattern, bool recursive = false)
    {
        SortedSet<string> found = new(StringComparer.Ordinal);
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (string root in Overlay is null ? [Root] : new[] { Root, Overlay })
        {
            string dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;

            foreach (string p in new[] { pattern, pattern + ".zs" })
                foreach (string file in Directory.EnumerateFiles(dir, p, option))
                {
                    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (rel.EndsWith(".zs", StringComparison.OrdinalIgnoreCase)) rel = rel[..^3];
                    found.Add(rel);
                }
        }

        return [.. found];
    }

    /// <summary>The names of the folders in a romfs folder, from the dump and the overlay together.</summary>
    public List<string> Folders(string folder)
    {
        SortedSet<string> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (string root in Overlay is null ? [Root] : new[] { Root, Overlay })
        {
            string dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;

            foreach (string sub in Directory.EnumerateDirectories(dir))
                found.Add(Path.GetFileName(sub));
        }

        return [.. found];
    }

    public byte[] Read(string path)
    {
        string resolved = Resolve(path)
            ?? throw new FileNotFoundException($"No {path} in {Root}, with or without .zs.", path);

        byte[] raw = File.ReadAllBytes(resolved);
        if (!ZsDic.IsCompressed(raw)) return raw;

        // The game's files all name their dictionary in the frame header; a mod's may be plain
        // zstd, which has to be read without one.
        return NamesDictionary(raw)
            ? _zs.Decompress(raw, resolved)
            : _zs.Decompress(raw, ZsDictionary.None);
    }

    /// <summary>Whether a zstd frame's header carries a dictionary id.</summary>
    private static bool NamesDictionary(ReadOnlySpan<byte> frame) => frame.Length > 4 && (frame[4] & 3) != 0;

    public byte[]? TryRead(string path) => Exists(path) ? Read(path) : null;

    public void Dispose() => _zs.Dispose();
}
