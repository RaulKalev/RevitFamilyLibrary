using Autodesk.Revit.DB;
using Family_Library.Revit.ExternalEvents;
using Family_Library.UI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Family_Library.Services
{
    public static class ProjectFamilyExporter
    {
        public static List<ProjectFamilyItem> CollectFamilies(Document doc)
        {
            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => !f.IsInPlace)
                .ToList();

            var items = new List<ProjectFamilyItem>();
            foreach (var f in families)
            {
                var typeCount = 0;
                try
                {
                    var symbols = f.GetFamilySymbolIds();
                    typeCount = symbols?.Count ?? 0;
                }
                catch { }

                items.Add(new ProjectFamilyItem
                {
                    UniqueId = f.UniqueId,
                    FamilyName = f.Name,
                    Category = f.FamilyCategory?.Name ?? "",
                    TypeCount = typeCount
                });
            }

            return items
                .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Saves the given project families into the library. Returns one message per family that failed; the
        /// others are still exported, so thumbnails and the index can be updated for them.
        /// </summary>
        public static List<string> ExportFamilies(Document doc, string libraryRoot, List<ProjectFamilyExportEntry> map)
        {
            var errors = new List<string>();
            if (doc == null || string.IsNullOrWhiteSpace(libraryRoot) || map == null)
                return errors;

            var familiesFolder = LibraryIndexer.GetFamiliesFolder(libraryRoot);

            // Build lookup of Family by UniqueId
            var allFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .ToDictionary(f => f.UniqueId, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in map)
            {
                if (!allFamilies.TryGetValue(entry.UniqueId, out var family))
                    continue;

                Document famDoc = null;
                try
                {
                    famDoc = doc.EditFamily(family);
                    if (famDoc == null)
                    {
                        errors.Add($"{family.Name}: EditFamily returned null");
                        continue;
                    }

                    var targetDir = string.IsNullOrWhiteSpace(entry.TargetFolder)
                        ? familiesFolder
                        : Path.Combine(familiesFolder, entry.TargetFolder);

                    Directory.CreateDirectory(targetDir);

                    var targetPath = Path.Combine(targetDir, family.Name + ".rfa");

                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(targetPath, saveOpts);
                }
                catch (Exception ex)
                {
                    errors.Add($"{family.Name}: {ex.Message}");
                }
                finally
                {
                    try { famDoc?.Close(false); } catch { }
                }
            }

            return errors;
        }
    }
}
