using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace Family_Library.Revit.ExternalEvents
{
    public enum LibraryTaskType
    {
        None = 0,
        BuildIndex = 1,
        GenerateThumbnailsAndIndex = 2,
        LoadSelectedFamilies = 3
    }

    public class LibraryTaskRequest
    {
        public LibraryTaskType TaskType { get; set; } = LibraryTaskType.None;

        public string LibraryRoot { get; set; }
        public int ThumbnailPixelSize { get; set; } = 1024;

        public string[] SelectedFamilyPaths { get; set; } = Array.Empty<string>();

        public bool PlaceAfterLoading { get; set; } = false;
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
