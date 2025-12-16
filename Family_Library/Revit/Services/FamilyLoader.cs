using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Family_Library.Services
{
    public static class FamilyLoader
    {
        private enum ConflictChoice
        {
            Ask = 0,
            Overwrite = 1,
            Skip = 2
        }

        private class ConditionalLoadOptions : IFamilyLoadOptions
        {
            private readonly bool _overwrite;

            public ConditionalLoadOptions(bool overwrite)
            {
                _overwrite = overwrite;
            }

            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = _overwrite;
                return _overwrite;
            }

            public bool OnSharedFamilyFound(
                Family sharedFamily,
                bool familyInUse,
                out FamilySource source,
                out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = _overwrite;
                return _overwrite;
            }
        }

        public static void LoadFamiliesIntoProject(
            UIApplication uiapp,
            Document doc,
            string[] familyPaths,
            bool placeAfterLoading)
        {
            if (uiapp == null || doc == null || familyPaths == null || familyPaths.Length == 0)
                return;

            int loaded = 0;
            int skipped = 0;
            int failed = 0;

            Family loadedFamilyForPlacement = null;

            var existingFamilies = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Select(f => f?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            ConflictChoice conflictAll = ConflictChoice.Ask;

            using (var t = new Transaction(doc, "Lae perekonnad"))
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

                        var familyName = Path.GetFileNameWithoutExtension(p);
                        var exists = !string.IsNullOrWhiteSpace(familyName)
                                     && existingFamilies.Contains(familyName);

                        bool overwriteThis = false;

                        if (exists)
                        {
                            if (conflictAll == ConflictChoice.Ask)
                            {
                                var result = ShowConflictDialog(familyName, p);

                                if (result == TaskDialogResult.Cancel)
                                {
                                    t.RollBack();
                                    return;
                                }

                                switch (result)
                                {
                                    case TaskDialogResult.CommandLink1:
                                        overwriteThis = true;
                                        break;

                                    case TaskDialogResult.CommandLink2:
                                        overwriteThis = false;
                                        break;

                                    case TaskDialogResult.CommandLink3:
                                        conflictAll = ConflictChoice.Overwrite;
                                        overwriteThis = true;
                                        break;

                                    case TaskDialogResult.CommandLink4:
                                        conflictAll = ConflictChoice.Skip;
                                        overwriteThis = false;
                                        break;

                                    default:
                                        overwriteThis = false;
                                        break;
                                }
                            }
                            else
                            {
                                overwriteThis = (conflictAll == ConflictChoice.Overwrite);
                            }

                            if (!overwriteThis)
                            {
                                skipped++;
                                continue;
                            }
                        }

                        Family fam;
                        bool ok;

                        if (exists)
                        {
                            ok = doc.LoadFamily(p, new ConditionalLoadOptions(true), out fam);
                        }
                        else
                        {
                            ok = doc.LoadFamily(p, out fam);
                        }

                        if (ok)
                        {
                            loaded++;
                            existingFamilies.Add(familyName);

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
                            using (var t2 = new Transaction(doc, "Aktiveeri tüüp"))
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

                    TaskDialog.Show("Perekonnateek",
                        "Perekond laaditi, kuid paigutatavat tüüpi ei leitud.");
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Perekonnateek",
                        "Perekond laaditi, kuid paigutamine ebaõnnestus:\n" + ex.Message);
                }
            }
            else
            {
                TaskDialog.Show("Perekonnateek",
                    $"Laetud: {loaded}\nVahele jäetud: {skipped}\nEbaõnnestunud: {failed}");
            }
        }

        private static TaskDialogResult ShowConflictDialog(string familyName, string fullPath)
        {
            var td = new TaskDialog("Perekonnateek")
            {
                MainInstruction = "Perekond on juba projektis olemas",
                MainContent =
                    $"Perekond \"{familyName}\" on juba projekti laaditud.\n\n" +
                    "Mida soovid teha?",
                AllowCancellation = true
            };

            td.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "Kirjuta üle",
                "Asenda projekti olemasolev perekond teegis oleva versiooniga.");

            td.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "Jäta vahele",
                "Kasuta projekti olemasolevat perekonda.");

            td.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink3,
                "Kirjuta kõik üle",
                "Kirjuta üle kõik projektis juba olemasolevad perekonnad.");

            td.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink4,
                "Jäta kõik vahele",
                "Ära lae ühtegi perekonda, mis on juba projektis olemas.");

            td.ExpandedContent = fullPath;

            return td.Show();
        }
    }
}
