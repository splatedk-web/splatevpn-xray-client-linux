namespace SplateClientDesktop.Services;

public static class AppPaths
{
    private static string Root(string variable,string fallback)
    {
        var value=Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(value)&&Path.IsPathFullyQualified(value) ? value : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),fallback);
    }
    public static string ConfigRoot => Root("XDG_CONFIG_HOME",".config");
    public static string DataRoot => Root("XDG_DATA_HOME",".local/share");
    public static void Initialize()
    {
        foreach(var root in new[]{ConfigRoot,DataRoot}) {
            var dir=Path.Combine(root,"SplateVPN");Directory.CreateDirectory(dir);
            if(OperatingSystem.IsLinux())File.SetUnixFileMode(dir,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        }
    }
}
