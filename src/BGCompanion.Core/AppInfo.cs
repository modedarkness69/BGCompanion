namespace BGCompanion.Core;

public static class AppInfo
{
    public const string Name = "BG Companion";
    public const string Credit = "made by modedarkness";
    public const string GitHubOwner = "modedarkness69";
    public const string GitHubRepo = "BGCompanion";
    public const string ReleaseAsset = "BGCompanion-win-x64.zip";
    public const string MainExe = "BGCompanion.exe";
    public const string UpdaterExe = "Updater.exe";

    public static Version CurrentVersion =>
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string VersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGCompanion");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
