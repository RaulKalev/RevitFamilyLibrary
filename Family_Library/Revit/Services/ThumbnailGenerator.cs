using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace Family_Library.Services
{
    public static class ThumbnailGenerator
    {
        public static void GenerateThumbnails(Application revitApp, string libraryRoot, int pixelSize)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
                return;

            var familiesFolder = GetFamiliesFolder(libraryRoot);
            var thumbsFolder = Path.Combine(libraryRoot, "Thumbs");
            Directory.CreateDirectory(thumbsFolder);

            var rfas = Directory.GetFiles(familiesFolder, "*.rfa", SearchOption.AllDirectories);
            if (rfas.Length == 0)
            {
                TaskDialog.Show("Family Library", $"No .rfa files found in:\n{familiesFolder}");
                return;
            }

            bool errorShown = false;

            foreach (var rfa in rfas)
            {
                var rel = GetRelativePath(familiesFolder, rfa);
                var outPng = Path.Combine(thumbsFolder, Path.ChangeExtension(rel, ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPng));

                Document doc = null;

                try
                {
                    doc = revitApp.OpenDocumentFile(rfa);
                    if (doc == null || !doc.IsFamilyDocument)
                        continue;

                    View3D view3d = null;

                    using (var t = new Transaction(doc, "Prepare Thumbnail View"))
                    {
                        t.Start();

                        view3d = GetOrCreateThumbnailView(doc); // now safe (creates/renames inside transaction)

                        HideJunk(doc, view3d);

                        var bb = GetCombinedBoundingBox(doc, view3d);
                        if (bb != null)
                        {
                            var padded = Pad(bb, 0.2);
                            view3d.SetSectionBox(padded);
                            view3d.IsSectionBoxActive = true;
                        }

                        t.Commit();
                    }

                    if (view3d != null)
                        ExportViewToPng(doc, view3d.Id, outPng, pixelSize);
                }
                catch (Exception ex)
                {
                    if (!errorShown)
                    {
                        errorShown = true;
                        TaskDialog.Show("Family Library", $"Thumbnail export failed for:\n{rfa}\n\n{ex.Message}");
                    }
                }
                finally
                {
                    try { doc?.Close(false); } catch { }
                }
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

        private static View3D GetOrCreateThumbnailView(Document doc)
        {
            // No modification needed if already exists
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate &&
                                     (v.Name.Equals("Thumbnail", StringComparison.OrdinalIgnoreCase) ||
                                      v.Name.Equals("PIG_3D", StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
                return existing;

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null)
                throw new InvalidOperationException("No 3D ViewFamilyType found in family document.");

            var v = View3D.CreateIsometric(doc, vft.Id);

            // Renaming is also a modification, must be inside transaction (we are).
            try { v.Name = "Thumbnail"; } catch { }

            return v;
        }

        private static void HideJunk(Document doc, View view)
        {
            TryHideByName(doc, view, "Levels");
            TryHideByName(doc, view, "Grids");
            TryHideByName(doc, view, "Dimensions");
            TryHideByName(doc, view, "Text Notes");
            TryHideByName(doc, view, "Reference Lines");

            HideElementsOfClass<ReferencePlane>(doc, view);
        }

        private static void TryHideByName(Document doc, View view, string categoryName)
        {
            var cat = doc.Settings.Categories
                .Cast<Category>()
                .FirstOrDefault(c => c != null && c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

            if (cat == null) return;

            try { view.SetCategoryHidden(cat.Id, true); }
            catch { }
        }

        private static void HideElementsOfClass<T>(Document doc, View view) where T : Element
        {
            try
            {
                var ids = new FilteredElementCollector(doc)
                    .OfClass(typeof(T))
                    .WhereElementIsNotElementType()
                    .Select(e => e.Id)
                    .ToList();

                if (ids.Count == 0) return;

                view.HideElements(ids);
            }
            catch { }
        }

        private static BoundingBoxXYZ GetCombinedBoundingBox(Document doc, View view)
        {
            BoundingBoxXYZ result = null;

            var els = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in els)
            {
                var bb = e.get_BoundingBox(view);
                if (bb == null) continue;

                if (result == null)
                {
                    result = new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max };
                    continue;
                }

                result.Min = new XYZ(
                    Math.Min(result.Min.X, bb.Min.X),
                    Math.Min(result.Min.Y, bb.Min.Y),
                    Math.Min(result.Min.Z, bb.Min.Z));

                result.Max = new XYZ(
                    Math.Max(result.Max.X, bb.Max.X),
                    Math.Max(result.Max.Y, bb.Max.Y),
                    Math.Max(result.Max.Z, bb.Max.Z));
            }

            return result;
        }

        private static BoundingBoxXYZ Pad(BoundingBoxXYZ bb, double padFeet)
        {
            return new BoundingBoxXYZ
            {
                Min = new XYZ(bb.Min.X - padFeet, bb.Min.Y - padFeet, bb.Min.Z - padFeet),
                Max = new XYZ(bb.Max.X + padFeet, bb.Max.Y + padFeet, bb.Max.Z + padFeet)
            };
        }

        private static void ExportViewToPng(Document doc, ElementId viewId, string outputPng, int pixelSize)
        {
            var dir = Path.GetDirectoryName(outputPng);
            Directory.CreateDirectory(dir);

            var baseName = Path.Combine(dir, Path.GetFileNameWithoutExtension(outputPng));

            // Record pngs before export (so we can detect new one)
            var before = Directory.GetFiles(dir, "*.png").ToHashSet(StringComparer.OrdinalIgnoreCase);

            var opts = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = baseName,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_300,
                PixelSize = pixelSize,
                ZoomType = ZoomFitType.FitToPage
            };

            opts.SetViewsAndSheets(new[] { viewId });
            doc.ExportImage(opts);

            // Revit can output unexpected names. Rename the newest new PNG to our target.
            var after = Directory.GetFiles(dir, "*.png");
            var created = after.Where(p => !before.Contains(p)).ToList();
            var newest = created
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest == null)
                return;

            if (string.Equals(newest.FullName, outputPng, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (File.Exists(outputPng))
                    File.Delete(outputPng);

                File.Move(newest.FullName, outputPng);
            }
            catch
            {
                // ignore
            }
        }
    }
}
