using Newtonsoft.Json;
using ProxyMov_DownloadServer.Misc;
using System.Reflection;

namespace ProxyMov_DownloadServer.Misc
{
    internal static class SettingsHelper
    {
        private static string? GetSaveFilePath()
        {
            string path;
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                path = @"/app/appdata/settings.json";

                if (!Directory.Exists(Path.GetDirectoryName(path)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                };

                return path;
            }
            else
            {
                return "settings.json";
            }
        }

        internal static T? ReadSettings<T>()
        {
            string? path = GetSaveFilePath();

            if (!File.Exists(path) || string.IsNullOrEmpty(path))
                return default;

            using StreamReader r = new(path);
            string json = r.ReadToEnd();

            SettingsModel? settings = JsonConvert.DeserializeObject<SettingsModel>(json);

            if (settings is null) return default;

            if (typeof(T) == typeof(SettingsModel))
            {
                return (T)Convert.ChangeType(settings, typeof(T));
            }

            return settings.GetSetting<T>();
        }

        public static T? GetSetting<T>(this SettingsModel settings)
        {
            return (T?)settings?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .First(_ => _.PropertyType == typeof(T))
                .GetValue(settings, null);
        }

        public static void SaveSettings(SettingsModel settings)
        {
            string? path = GetSaveFilePath();

            if (!File.Exists(path) || string.IsNullOrEmpty(path))
                return;

            string json = JsonConvert.SerializeObject(settings);

            File.WriteAllText(path, json);
        }
    }
}
