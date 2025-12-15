using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace Family_Library.Services
{
    public static class FamilyLoader
    {
        private class AlwaysLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        public static void LoadFamiliesIntoProject(UIApplication uiapp, Document doc, string[] familyPaths, bool placeAfterLoading)
        {
            if (uiapp == null || doc == null || familyPaths == null || familyPaths.Length == 0)
                return;

            var opts = new AlwaysLoadOptions();

            int loaded = 0;
            int failed = 0;

            Family loadedFamilyForPlacement = null;

            using (var t = new Transaction(doc, "Load Families"))
            {
                t.Start();

                foreach (var p in familyPaths)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                        {
                            failed++;
                            continue;
                        }

                        if (doc.LoadFamily(p, opts, out Family fam))
                        {
                            loaded++;
                            if (loadedFamilyForPlacement == null)
                                loadedFamilyForPlacement = fam;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }

                t.Commit();
            }

            // Place only when:
            // - checkbox is on
            // - exactly 1 family selected
            // - family loaded successfully
            if (placeAfterLoading && familyPaths.Length == 1 && loadedFamilyForPlacement != null)
            {
                try
                {
                    var symbolId = loadedFamilyForPlacement.GetFamilySymbolIds().FirstOrDefault();
                    if (symbolId != null && symbolId != ElementId.InvalidElementId)
                    {
                        var symbol = doc.GetElement(symbolId) as FamilySymbol;
                        if (symbol != null)
                        {
                            using (var t2 = new Transaction(doc, "Activate Type"))
                            {
                                t2.Start();
                                if (!symbol.IsActive)
                                    symbol.Activate();
                                t2.Commit();
                            }

                            DeferredPlacement.Start(uiapp, symbol.Id);
                            return;
                        }
                    }

                    TaskDialog.Show("Family Library", "Family loaded, but no placeable type was found.");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Family Library", "Loaded family, but placement could not start:\n" + ex.Message);
                }
            }

            else
            {
                // Minimal feedback; remove if you want fully silent
                TaskDialog.Show("Family Library", $"Loaded: {loaded}\nFailed: {failed}");
            }
        }
    }
}
