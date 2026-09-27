using Autodesk.Revit.UI;

namespace Family_Library.Revit
{
    public static class UiWindowHost
    {
        private static UI.MainWindow _window;

        /// <summary>Revit's main window; owner for dialogs while the Family Library window is hidden.</summary>
        public static System.IntPtr RevitHandle { get; private set; }

        /// <summary>The Family Library window when it is on screen (dialogs center on it), else null.</summary>
        public static System.Windows.Window DialogOwnerWindow =>
            _window != null && _window.IsLoaded && _window.IsVisible && _window.WindowState != System.Windows.WindowState.Minimized
                ? _window
                : null;

        public static void Show(UIApplication uiapp)
        {
            if (uiapp.MainWindowHandle != System.IntPtr.Zero)
                RevitHandle = uiapp.MainWindowHandle;

            if (_window == null || !_window.IsLoaded)
            {
                _window = new UI.MainWindow(uiapp);

                // Owned by Revit's main window (as in CAD Manager): an unowned modeless window falls behind Revit
                // whenever Revit takes focus (e.g. on Escape), which looks like the window minimized.
                var revitHandle = uiapp.MainWindowHandle;
                if (revitHandle != System.IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(_window).Owner = revitHandle;

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

                // The window refuses other minimize requests (Revit minimizes it on Escape); this one is intended.
                _window.MinimizeForPlacement();
                // DO NOT call _window.Hide();
            }
            catch { }
        }

        /// <summary>Called when the placement tool has ended: bring the window back and give it focus.</summary>
        public static void RestoreAfterPlacement()
        {
            try
            {
                if (_window == null || !_window.IsLoaded) return;

                if (_window.WindowState == System.Windows.WindowState.Minimized)
                    System.Windows.SystemCommands.RestoreWindow(_window);

                _window.Activate();
                _window.Focus();
            }
            catch
            {
            }
        }
    }
}
