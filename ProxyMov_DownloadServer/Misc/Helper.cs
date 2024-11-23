using System.Runtime.InteropServices;

namespace ProxyMov_DownloadServer.Misc
{
    internal static class Helper
    {
        internal static string GetFFMPEGPath()
        {
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Path.Combine(Directory.GetCurrentDirectory(), "appdata", Globals.FFMPEGBinDocker);

            return Path.Combine(Directory.GetCurrentDirectory(), Globals.FFMPEGBin);
        }

        internal static string GetFFProbePath()
        {
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Path.Combine(Directory.GetCurrentDirectory(), "appdata", Globals.FFProbeBinDocker);

            return Path.Combine(Directory.GetCurrentDirectory(), Globals.FFProbeBin);
        }

        internal static string GetBrowserPath()
        {
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Path.Combine(Directory.GetCurrentDirectory(), "appdata", Globals.BrowserPathDocker);

            return Path.Combine(Directory.GetCurrentDirectory(), Globals.BrowserPath);
        }
        internal static string GetBrowserBinPath()
        {
            string chromeFolderPath;
            string[] files;

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                chromeFolderPath = Path.Combine(GetBrowserPath(), "Chrome");
                files = Directory.GetFiles(chromeFolderPath, "chrome", SearchOption.AllDirectories);
            }
            else
            {
                chromeFolderPath = Path.Combine(GetBrowserPath(), "Chrome");
                files = Directory.GetFiles(chromeFolderPath, "chrome.exe", SearchOption.AllDirectories);
            }



            string? chromeBinPath = files.FirstOrDefault();

            if (!File.Exists(chromeBinPath))
                throw new FileNotFoundException($"No browser executable found in: {chromeFolderPath}!");

            return chromeBinPath;
        }
    }
}
