using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Family_Library.UI.Models;
using System;
using System.Collections.Generic;
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

            var rfas = Directory.GetFiles(familiesFolder, "*.rfa", SearchOption.AllDirectories);

            var items = new List<LibraryItem>();

            foreach (var rfa in rfas)
            {
                var rel = GetRelativePath(familiesFolder, rfa);
                var thumb = Path.Combine(thumbsFolder, Path.ChangeExtension(rel, ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(thumb));

                var item = new LibraryItem
                {
                    DisplayName = Path.GetFileNameWithoutExtension(rfa),
                    Category = "",
                    RelativePath = rel.Replace('\\', '/'),
                    FullPath = rfa,
                    ThumbnailPath = File.Exists(thumb) ? thumb : "",
                    TypeNames = new List<string>()
                };

                // Read category + type names by opening the family
                TryReadFamilyMetadata(revitApp, rfa, item);

                items.Add(item);
            }

            var indexPath = Path.Combine(libraryRoot, "index.json");
            IndexStore.Write(indexPath, items.OrderBy(x => x.DisplayName).ToList());
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
    }
}
