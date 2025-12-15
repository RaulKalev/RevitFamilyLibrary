using Autodesk.Revit.UI;

namespace Family_Library.Revit
{
    public static class UiWindowHost
    {
        private static UI.MainWindow _window;

        public static void Show(UIApplication uiapp)
        {
            if (_window == null || !_window.IsLoaded)
            {
                _window = new UI.MainWindow(uiapp);
                _window.Show();
                return;
            }

            if (_window.WindowState == System.Windows.WindowState.Minimized)
                _window.WindowState = System.Windows.WindowState.Normal;

            _window.Show();
            _window.Activate();
        }

        // NEW: called before starting interactive placement
        public static void HideForPlacement()
        {
            try
            {
                if (_window == null) return;

                _window.Topmost = false;
                _window.WindowState = System.Windows.WindowState.Minimized;
                // DO NOT call _window.Hide();
            }
            catch { }
        }

    }
}
