using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Family_Library.UI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Family_Library.Services
{
    public static class LibraryIndexer
    {
        public static void BuildIndex(Application revitApp, string libraryRoot)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
                return;

            var familiesFolder = GetFamiliesFolder(libraryRoot);
            var thumbsFolder = Path.Combine(libraryRoot, "Thumbs");
            Directory.CreateDirectory(thumbsFolder);

            var indexPath = Path.Combine(libraryRoot, "index.json");

            // Load existing index so we can update only new/changed items
            var existingList = File.Exists(indexPath) ? IndexStore.Read(indexPath) : new List<LibraryItem>();
            var map = existingList
                .Where(x => !string.IsNullOrWhiteSpace(x.FullPath))
                .ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);

            var rfas = Directory.GetFiles(familiesFolder, "*.rfa", SearchOption.AllDirectories);

            foreach (var rfa in rfas)
            {
                var lastWriteUtc = File.GetLastWriteTimeUtc(rfa);

                // If unchanged -> skip completely (fast re-index)
                if (map.TryGetValue(rfa, out var existing))
                {
                    if (existing.LastWriteTimeUtc >= lastWriteUtc)
                        continue;
                }

                var rel = GetRelativePath(familiesFolder, rfa);
                var thumb = Path.Combine(thumbsFolder, Path.ChangeExtension(rel, ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(thumb));

                // New or update
                var item = existing ?? new LibraryItem();

                item.DisplayName = Path.GetFileNameWithoutExtension(rfa);
                item.Category = item.Category ?? "";
                item.RelativePath = rel.Replace('\\', '/');
                item.FullPath = rfa;
                item.ThumbnailPath = File.Exists(thumb) ? thumb : "";

                // NEW: timestamp + Revit version
                item.LastWriteTimeUtc = lastWriteUtc;
                item.SavedInRevitVersion = TryGetSavedInRevitVersion(rfa);

                // Expensive part: only for new/changed
                TryReadFamilyMetadata(revitApp, rfa, item);

                map[rfa] = item;
            }

            IndexStore.Write(indexPath, map.Values.OrderBy(x => x.DisplayName).ToList());
        }


        private static void TryReadFamilyMetadata(Application revitApp, string rfaPath, LibraryItem item)
        {
            Document famDoc = null;

            try
            {
                famDoc = revitApp.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                    return;

                // Category
                try
                {
                    var fam = famDoc.OwnerFamily;
                    item.Category = fam?.FamilyCategory?.Name ?? "";
                }
                catch { }

                // Types (symbols)
                // Types (FamilyManager) - reliable inside family documents
                try
                {
                    var fm = famDoc.FamilyManager;
                    if (fm != null)
                    {
                        var names = new List<string>();

                        foreach (FamilyType ft in fm.Types)
                        {
                            if (ft == null) continue;
                            var n = ft.Name;
                            if (!string.IsNullOrWhiteSpace(n))
                                names.Add(n);
                        }

                        item.TypeNames = names
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x)
                            .ToList();
                    }
                }
                catch { }

            }
            finally
            {
                try { famDoc?.Close(false); } catch { }
            }
        }

        private static string GetFamiliesFolder(string libraryRoot)
        {
            var families = Path.Combine(libraryRoot, "Families");

            if (Directory.Exists(families) && Directory.GetFiles(families, "*.rfa", SearchOption.AllDirectories).Any())
                return families;

            return libraryRoot;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith("\\") && !basePath.EndsWith("/"))
                basePath += Path.DirectorySeparatorChar;

            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath);

            var rel = baseUri.MakeRelativeUri(fullUri).ToString();
            rel = Uri.UnescapeDataString(rel);
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        private static string TryGetSavedInRevitVersion(string rfaPath)
        {
            try
            {
                var info = BasicFileInfo.Extract(rfaPath);
                if (info == null) return "";

                // Different Revit versions expose different properties.
                // Try the common ones (string), in a safe order.
                var candidates = new[]
                {
            "SavedInVersion",   // some versions
            "RevitVersion",     // some versions
            "Format",           // sometimes includes "Autodesk Revit xxxx"
            "RevitBuild",       // e.g. "Autodesk Revit 2024 (Build: ...)"
            "Build"             // some versions
        };

                foreach (var propName in candidates)
                {
                    var p = info.GetType().GetProperty(propName);
                    if (p == null) continue;

                    var val = p.GetValue(info, null);
                    var s = val as string;
                    if (string.IsNullOrWhiteSpace(s)) continue;

                    var year = ExtractYear(s);
                    if (!string.IsNullOrWhiteSpace(year))
                        return year;
                }

                // If all properties fail, try ToString() as last resort.
                var fallback = info.ToString() ?? "";
                return ExtractYear(fallback) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractYear(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";

            // Find first 4-digit year 20xx in the string
            for (int i = 0; i <= s.Length - 4; i++)
            {
                if (s[i] == '2' && s[i + 1] == '0')
                {
                    if (char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
                    {
                        var year = s.Substring(i, 4);
                        // sanity: Revit years in realistic range
                        if (int.TryParse(year, out var y) && y >= 2010 && y <= 2100)
                            return year;
                    }
                }
            }

            return "";
        }
        private static ObservableCollection<string> LoadTypeThumbs(string libraryRoot, string relativePath)
        {
            var relDir = Path.GetDirectoryName(relativePath) ?? "";
            var familyNameNoExt = Path.GetFileNameWithoutExtension(relativePath);

            var folder = Path.Combine(libraryRoot, "Thumbs_Types", relDir, familyNameNoExt);
            if (!Directory.Exists(folder))
                return new ObservableCollection<string>();

            var files = Directory.GetFiles(folder, "*.png")
                .OrderBy(f => f)
                .ToList();

            return new ObservableCollection<string>(files);
        }

    }
}
