namespace PhotinoNetUtils.Utils;

/// <summary>File helpers shared by the icon extractors.</summary>
internal static class FileUtil
{
    /// <summary>
    ///     Writes <paramref name="bytes" /> unless the file already holds exactly them, so a file that
    ///     tools watch — an installed icon, for instance — is not rewritten on every start.
    /// </summary>
    public static void WriteAllBytesIfChanged(string path, byte[] bytes)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            File.WriteAllBytes(path, bytes);
    }
}
