using Autodesk.Revit.UI;
using ricaun.Revit.UI;

namespace Family_Library.Revit
{
    [AppLoader]
    public class App : IExternalApplication
    {
        private RibbonPanel ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "RK Tools";

            try { application.CreateRibbonTab(tabName); }
            catch { /* tab already exists */ }

            ribbonPanel = application.CreateOrSelectPanel(tabName, "Project");

            string iconName;
            try
            {
                iconName = UIThemeManager.CurrentTheme == UITheme.Dark
                    ? "Light%20-%20FamilyLibrary.tiff"
                    : "Dark%20-%20FamilyLibrary.tiff";
            }
            catch
            {
                iconName = "Revit.ico";
            }

            ribbonPanel.CreatePushButton<Commands.Command>()
                .SetLargeImage($"pack://application:,,,/Family_Library;component/Resources/{iconName}")
                .SetText("Family\nLibrary")
                .SetToolTip("Browse and load Revit families from your library.")
                .SetLongDescription("Family Library lets you browse, search, filter and load RFA families directly into your Revit project.");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }
    }

}