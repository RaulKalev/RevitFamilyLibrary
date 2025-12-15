using Family_Library.UI.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace Family_Library.Services
{
    public static class IndexStore
    {
        public static List<LibraryItem> Read(string indexPath)
        {
            try
            {
                return JsonConvert.DeserializeObject<List<LibraryItem>>(File.ReadAllText(indexPath)) ?? new List<LibraryItem>();
            }
            catch
            {
                return new List<LibraryItem>();
            }
        }

        public static void Write(string indexPath, List<LibraryItem> items)
        {
            File.WriteAllText(indexPath, JsonConvert.SerializeObject(items, Newtonsoft.Json.Formatting.Indented));
        }
    }
}
