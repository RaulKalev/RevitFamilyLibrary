using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Family_Library.Services
{
    public static class ThumbnailGenerator
    {
        public static void GenerateThumbnails(Application revitApp, string libraryRoot, int pixelSize)
        {
            if (revitApp == null) return;
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot)) return;
            if (pixelSize <= 0) return;

            string familiesFolder = GetFamiliesFolder(libraryRoot);

            string thumbsFolder = Path.Combine(libraryRoot, "Thumbs");
            string typeThumbsFolder = Path.Combine(libraryRoot, "Thumbs_Types");
            Directory.CreateDirectory(thumbsFolder);
            Directory.CreateDirectory(typeThumbsFolder);

            string[] rfas = Directory.GetFiles(familiesFolder, "*.rfa", SearchOption.AllDirectories);
            if (rfas.Length == 0)
            {
                TaskDialog.Show("Family Library", $"No .rfa files found in:\n{familiesFolder}");
                return;
            }

            bool errorShown = false;

            foreach (string rfa in rfas)
            {
                string rel = GetRelativePath(familiesFolder, rfa);

                // Main family thumb (one per .rfa)
                string familyOutPng = Path.Combine(thumbsFolder, Path.ChangeExtension(rel, ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(familyOutPng));

                // Per-type thumbs folder: Thumbs_Types\<relative folder>\<FamilyName>\<Type>.png
                string typeFolder = Path.Combine(typeThumbsFolder, Path.GetDirectoryName(rel) ?? "");
                string familyNameNoExt = Path.GetFileNameWithoutExtension(rel);
                string familyTypeOutDir = Path.Combine(typeFolder, familyNameNoExt);
                Directory.CreateDirectory(familyTypeOutDir);

                Document doc = null;

                try
                {
                    doc = revitApp.OpenDocumentFile(rfa);
                    if (doc == null || !doc.IsFamilyDocument) continue;

                    var fm = doc.FamilyManager;
                    if (fm == null || fm.Types == null) continue;

                    ViewPlan plan;

                    // Prepare plan view + base hiding rules
                    using (var t = new Transaction(doc, "Prepare Thumbnail View"))
                    {
                        t.Start();

                        plan = GetOrCreateRefLevelPlanView(doc, "Ref. Level");
                        if (plan != null)
                            ConfigurePlanForThumbnail(doc, plan);

                        t.Commit();
                    }

                    if (plan == null) continue;

                    // Oversample export (2x) -> downsample to final square.
                    // This makes thin symbols (like connector glyphs) much less noticeable.
                    int exportPixels = pixelSize * 2;

                    // Snapshot potential connector ids (best-effort; not always available)
                    List<ElementId> connectorIds = GetConnectorIds(doc);

                    // Rollback group: no permanent changes to the family
                    using (var tg = new TransactionGroup(doc, "Temp: Thumbnail Export"))
                    {
                        tg.Start();

                        // Hide connectors in the view if we can (must be in transaction)
                        if (connectorIds.Count > 0)
                        {
                            using (var tHide = new Transaction(doc, "Hide connectors in view"))
                            {
                                tHide.Start();
                                try { plan.HideElements(connectorIds); } catch { }
                                tHide.Commit();
                            }
                        }

                        bool wroteFamilyThumb = false;

                        foreach (FamilyType ft in fm.Types)
                        {
                            if (ft == null) continue;

                            using (var tType = new Transaction(doc, "Set type for thumbnail"))
                            {
                                tType.Start();
                                fm.CurrentType = ft;
                                doc.Regenerate();
                                tType.Commit();
                            }

                            string safeTypeName = MakeFileNameSafe(ft.Name);
                            string typeOut = Path.Combine(familyTypeOutDir, safeTypeName + ".png");

                            ExportViewToSquarePng(doc, plan.Id, typeOut, exportPixels, pixelSize);

                            // First type becomes the main family thumbnail (optional)
                            if (!wroteFamilyThumb && File.Exists(typeOut))
                            {
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(familyOutPng));
                                    File.Copy(typeOut, familyOutPng, true);
                                    wroteFamilyThumb = true;
                                }
                                catch { }
                            }
                        }

                        // Always rollback (do NOT commit) so nothing stays modified.
                        try { tg.RollBack(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    if (!errorShown)
                    {
                        errorShown = true;
                        TaskDialog.Show(
                            "Family Library",
                            $"Thumbnail export failed for:\n{rfa}\n\n{ex.Message}"
                        );
                    }
                }
                finally
                {
                    try { doc?.Close(false); } catch { }
                }
            }
        }

        private static ViewPlan GetOrCreateRefLevelPlanView(Document doc, string levelName)
        {
            // Existing non-template floor plan on Ref. Level
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.ViewType == ViewType.FloorPlan &&
                    string.Equals(GetViewLevelName(doc, v), levelName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            // Find "Ref. Level"
            var lvl = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(x => string.Equals(x.Name, levelName, StringComparison.OrdinalIgnoreCase));

            if (lvl == null)
                return null;

            // Find floor plan view family type
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan);

            if (vft == null)
                return null;

            var view = ViewPlan.Create(doc, vft.Id, lvl.Id);
            try { view.Name = "Thumbnail_RefLevel"; } catch { }
            return view;
        }

        private static string GetViewLevelName(Document doc, ViewPlan v)
        {
            try
            {
                var lvl = doc.GetElement(v.GenLevel.Id) as Level;
                return lvl?.Name ?? "";
            }
            catch { return ""; }
        }

        private static void ConfigurePlanForThumbnail(Document doc, ViewPlan view)
        {
            // Preview visibility ON / Uncut if possible
            try
            {
                var tvm = view.TemporaryViewModes;
                if (tvm != null)
                {
                    if (tvm.IsValidState(PreviewFamilyVisibilityMode.Uncut))
                        tvm.PreviewFamilyVisibility = PreviewFamilyVisibilityMode.Uncut;
                    else if (tvm.IsValidState(PreviewFamilyVisibilityMode.On))
                        tvm.PreviewFamilyVisibility = PreviewFamilyVisibilityMode.On;
                }
            }
            catch { }

            // Make connector glyphs smaller (practical workaround)
            try { view.Scale = 10; } catch { } // 1:10

            // Base hiding (categories)
            TryHideCategory(view, BuiltInCategory.OST_Dimensions);
            TryHideCategory(view, BuiltInCategory.OST_Constraints);
            TryHideCategory(view, BuiltInCategory.OST_CLines);
            TryHideCategory(view, BuiltInCategory.OST_ReferenceLines);
            TryHideCategory(view, BuiltInCategory.OST_ConnectorElem);

            // Extra: hide by class (best effort)
            TryHideAllOfClassInView(doc, view, typeof(Dimension));
            TryHideAllOfClassInView(doc, view, typeof(ReferencePlane));
            TryHideAllOfClassInView(doc, view, typeof(ReferencePoint));

            // Keep fine so 2D detail items remain readable
            try { view.DetailLevel = ViewDetailLevel.Fine; } catch { }
        }

        private static void TryHideCategory(View view, BuiltInCategory bic)
        {
            try
            {
                var cat = Category.GetCategory(view.Document, bic);
                if (cat != null)
                    view.SetCategoryHidden(cat.Id, true);
            }
            catch { }
        }

        private static void TryHideAllOfClassInView(Document doc, View view, Type elementClass)
        {
            try
            {
                var ids = new FilteredElementCollector(doc)
                    .OfClass(elementClass)
                    .WhereElementIsNotElementType()
                    .Select(x => x.Id)
                    .Where(id => id != null && id != ElementId.InvalidElementId)
                    .Distinct()
                    .ToList();

                if (ids.Count == 0) return;

                // Hide in small batches so one unhideable element doesn't break the call
                const int batchSize = 200;
                for (int i = 0; i < ids.Count; i += batchSize)
                {
                    var batch = ids.Skip(i).Take(batchSize).ToList();
                    try { view.HideElements(batch); } catch { }
                }
            }
            catch { }
        }

        private static List<ElementId> GetConnectorIds(Document doc)
        {
            // NOTE:
            // In many families the visible "connector glyph" is NOT an actual ConnectorElement you can collect/hide.
            // This is best-effort only. The scale+oversample trick is the reliable fallback.
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ConnectorElement))
                    .WhereElementIsNotElementType()
                    .Select(e => e.Id)
                    .Where(id => id != null && id != ElementId.InvalidElementId)
                    .Distinct()
                    .ToList();
            }
            catch
            {
                return new List<ElementId>();
            }
        }

        private static void ExportViewToSquarePng(Document doc, ElementId viewId, string outputPng, int exportPixelSize, int finalSquareSize)
        {
            string dir = Path.GetDirectoryName(outputPng);
            Directory.CreateDirectory(dir);

            string baseName = Path.Combine(dir, "_tmp_export_" + Guid.NewGuid().ToString("N"));
            var before = Directory.GetFiles(dir, "*.png").ToHashSet(StringComparer.OrdinalIgnoreCase);

            var opts = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = baseName,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_300,
                PixelSize = exportPixelSize,
                ZoomType = ZoomFitType.FitToPage
            };

            opts.SetViewsAndSheets(new[] { viewId });
            doc.ExportImage(opts);

            var after = Directory.GetFiles(dir, "*.png");
            var newest = after
                .Where(f => !before.Contains(f))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest == null || !newest.Exists)
                return;

            try
            {
                if (File.Exists(outputPng))
                    File.Delete(outputPng);

                File.Move(newest.FullName, outputPng);
            }
            catch
            {
                return;
            }

            // Downsample/pad to final square
            try
            {
                MakeSquarePngInPlace(outputPng, finalSquareSize);
            }
            catch { }
        }

        private static void MakeSquarePngInPlace(string pngPath, int size)
        {
            using (var src = new System.Drawing.Bitmap(pngPath))
            using (var dst = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
                // White canvas for clean look (matches your request)
                g.Clear(System.Drawing.Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                double sx = (double)size / src.Width;
                double sy = (double)size / src.Height;
                double s = Math.Min(sx, sy);

                int w = (int)Math.Round(src.Width * s);
                int h = (int)Math.Round(src.Height * s);
                int x = (size - w) / 2;
                int y = (size - h) / 2;

                g.DrawImage(src, new System.Drawing.Rectangle(x, y, w, h));

                string tmp = pngPath + ".tmp";
                dst.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);

                try
                {
                    File.Delete(pngPath);
                    File.Move(tmp, pngPath);
                }
                catch
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
        }

        private static string MakeFileNameSafe(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Type";

            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Trim();
        }

        private static string GetFamiliesFolder(string libraryRoot)
        {
            string families = Path.Combine(libraryRoot, "Families");
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
