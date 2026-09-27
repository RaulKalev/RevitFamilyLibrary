using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Family_Library.UI.Dialogs;

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
            int upToDate = 0;
            string firstError = null;

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
                                var result = ShowConflictDialog(familyName, p, familyPaths.Length > 1);

                                if (result == ConflictResult.Cancel)
                                {
                                    t.RollBack();
                                    return;
                                }

                                switch (result)
                                {
                                    case ConflictResult.Overwrite:
                                        overwriteThis = true;
                                        break;

                                    case ConflictResult.OverwriteAll:
                                        conflictAll = ConflictChoice.Overwrite;
                                        overwriteThis = true;
                                        break;

                                    case ConflictResult.SkipAll:
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

                        // LoadFamily also returns false (and no family) when the project already has this exact
                        // version: nothing to reload. That is not a failure – use the family in the project.
                        if (fam == null && exists)
                            fam = FindFamily(doc, familyName);

                        if (ok)
                        {
                            loaded++;
                        }
                        else if (exists && fam != null)
                        {
                            upToDate++;
                        }
                        else
                        {
                            failed++;
                            if (firstError == null)
                                firstError = familyName + ": Revit ei laadinud perekonda.";
                            continue;
                        }

                        existingFamilies.Add(familyName);
                        if (loadedFamilyForPlacement == null)
                            loadedFamilyForPlacement = fam ?? FindFamily(doc, familyName);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (firstError == null)
                            firstError = Path.GetFileNameWithoutExtension(p) + ": " + ex.Message;
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

                    LibraryDialogs.Error("Paigutamine ebaõnnestus",
                        "Perekond laaditi, kuid sellel ei leitud paigutatavat tüüpi.");
                }
                catch (Exception ex)
                {
                    LibraryDialogs.Error("Paigutamine ebaõnnestus",
                        "Perekond laaditi, kuid paigutamist ei saanud alustada.", ex.Message);
                }
            }
            else
            {
                ShowSummary(loaded, upToDate, skipped, failed, firstError);
            }
        }

        private static void ShowSummary(int loaded, int upToDate, int skipped, int failed, string firstError)
        {
            // Only the counts that happened, in plain words.
            var lines = new List<string>();
            if (loaded > 0) lines.Add("Laaditud: " + loaded);
            if (upToDate > 0) lines.Add("Juba ajakohane: " + upToDate);
            if (skipped > 0) lines.Add("Vahele jäetud: " + skipped);
            if (failed > 0) lines.Add("Ebaõnnestunud: " + failed);
            var message = lines.Count > 0 ? string.Join("\n", lines) : "Midagi ei laaditud.";

            if (failed > 0)
                LibraryDialogs.Error(loaded + upToDate > 0 ? "Osa perekondi jäi laadimata" : "Laadimine ebaõnnestus", message, firstError);
            else
                LibraryDialogs.Info(loaded > 0 ? "Perekonnad laaditud" : "Midagi ei muutunud", message, null,
                    loaded > 0 ? DialogKind.Success : DialogKind.Info);
        }

        private static Family FindFamily(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        private enum ConflictResult { Cancel, Overwrite, Skip, OverwriteAll, SkipAll }

        private static ConflictResult ShowConflictDialog(string familyName, string fullPath, bool several)
        {
            var choices = new List<DialogChoice>
            {
                new DialogChoice("Kirjuta üle", "Asenda projektis olev perekond teegis oleva versiooniga."),
                new DialogChoice("Jäta vahele", "Kasuta projektis juba olevat perekonda.")
            };
            if (several)
            {
                choices.Add(new DialogChoice("Kirjuta kõik üle", "Tee sama kõigi projektis juba olevate perekondadega."));
                choices.Add(new DialogChoice("Jäta kõik vahele", "Ära laadi ühtegi perekonda, mis on juba projektis."));
            }

            var index = LibraryDialogs.Choose(
                "Perekond on juba projektis",
                $"„{familyName}” on projekti juba laaditud. Mida teha?",
                choices,
                fullPath,
                "Katkesta laadimine");

            switch (index)
            {
                case 0: return ConflictResult.Overwrite;
                case 1: return ConflictResult.Skip;
                case 2: return ConflictResult.OverwriteAll;
                case 3: return ConflictResult.SkipAll;
                default: return ConflictResult.Cancel;
            }
        }
    }
}
