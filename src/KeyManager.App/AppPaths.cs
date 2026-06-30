namespace KeyManager.App;

internal static class AppPaths
{
    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyManager");

    public static string VaultPath => Path.Combine(Dir, "vault.json");

    public static void EnsureDir() => Directory.CreateDirectory(Dir);
}
