using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
// Remove or alias the following line if you need System.Xml.Formatting elsewhere
using System.Xml;

namespace Family_Library.Services
{
    public class AppSettings
    {
        public string LibraryRoot { get; set; } = "";
        public int ThumbnailPixelSize { get; set; } = 384;

        // NEW: user-maintained list
        public List<string> UserCategories { get; set; } = new List<string>();
    }

    public static class SettingsStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RK Tools",
            "Family_Library");

        private static readonly string PathSettings = System.IO.Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(PathSettings))
                    return new AppSettings();

                return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(PathSettings)) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathSettings, JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented));
        }
    }
}
