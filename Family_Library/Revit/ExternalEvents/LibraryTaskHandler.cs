using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace Family_Library.Revit.ExternalEvents
{
    public enum LibraryTaskType
    {
        None = 0,
        BuildIndex = 1,
        GenerateThumbnailsAndIndex = 2,
        LoadSelectedFamilies = 3,
        CollectProjectFamilies = 4,
        ImportFamiliesFromProject = 5
    }

    public class ProjectFamilyExportEntry
    {
        public string UniqueId { get; set; }
        public string TargetFolder { get; set; }
    }

    public class LibraryTaskRequest
    {
        public LibraryTaskType TaskType { get; set; } = LibraryTaskType.None;

        public string LibraryRoot { get; set; }
        public int ThumbnailPixelSize { get; set; } = 1024;

        public string[] SelectedFamilyPaths { get; set; } = Array.Empty<string>();

        public bool PlaceAfterLoading { get; set; } = false;

        public List<UI.Models.ProjectFamilyItem> CollectedFamilies { get; set; }
            = new List<UI.Models.ProjectFamilyItem>();

        public List<ProjectFamilyExportEntry> FamilyExportMap { get; set; }
            = new List<ProjectFamilyExportEntry>();

        /// <summary>Result of ImportFamiliesFromProject: families saved into the library.</summary>
        public int ExportedCount { get; set; }
    }


    public class LibraryTaskHandler : IExternalEventHandler
    {
        public LibraryTaskRequest Request { get; } = new LibraryTaskRequest();

        public void Execute(UIApplication app)
        {
            LastRunFailed = false;
            try
            {
                if (Request.TaskType == LibraryTaskType.None)
                    return;

                switch (Request.TaskType)
                {
                    case LibraryTaskType.BuildIndex:
                        Services.LibraryIndexer.BuildIndex(app.Application, Request.LibraryRoot);
                        break;

                    case LibraryTaskType.GenerateThumbnailsAndIndex:
                        Services.ThumbnailGenerator.GenerateThumbnails(app.Application, Request.LibraryRoot, Request.ThumbnailPixelSize);
                        Services.LibraryIndexer.BuildIndex(app.Application, Request.LibraryRoot);
                        break;

                    case LibraryTaskType.LoadSelectedFamilies:
                        Services.FamilyLoader.LoadFamiliesIntoProject(
                            app,
                            app.ActiveUIDocument?.Document,
                            Request.SelectedFamilyPaths,
                            Request.PlaceAfterLoading);
                        break;

                    case LibraryTaskType.CollectProjectFamilies:
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null)
                            throw new InvalidOperationException("Aktiivne Revit dokument puudub. Ava projekt enne importimist.");
                        Request.CollectedFamilies = Services.ProjectFamilyExporter.CollectFamilies(doc);
                        break;
                    }

                    case LibraryTaskType.ImportFamiliesFromProject:
                    {
                        var srcDoc = app.ActiveUIDocument?.Document;
                        if (srcDoc == null)
                            throw new InvalidOperationException("Aktiivne Revit dokument puudub. Ava projekt enne importimist.");
                        var errors = Services.ProjectFamilyExporter.ExportFamilies(srcDoc, Request.LibraryRoot, Request.FamilyExportMap);
                        Request.ExportedCount = Request.FamilyExportMap.Count - errors.Count;

                        // Thumbnails and index for everything that was exported, even if some families failed
                        if (Request.ExportedCount > 0)
                        {
                            Services.ThumbnailGenerator.GenerateThumbnails(app.Application, Request.LibraryRoot, Request.ThumbnailPixelSize);
                            Services.LibraryIndexer.BuildIndex(app.Application, Request.LibraryRoot);
                        }

                        if (errors.Count > 0)
                            throw new InvalidOperationException(
                                errors.Count + " perekonda jäi importimata:\n" + string.Join("\n", errors));
                        break;
                    }

                }
            }
            catch (Exception ex)
            {
                LastRunFailed = true;
                UI.Dialogs.LibraryDialogs.Error("Toiming ebaõnnestus", ex.Message, ex.ToString());
            }
            finally
            {
                Request.TaskType = LibraryTaskType.None;
                OnCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>True when the last run ended with an exception (already shown to the user).</summary>
        public bool LastRunFailed { get; private set; }

        public event EventHandler OnCompleted;
        public string GetName() => "Family Library Tasks";
    }
}
