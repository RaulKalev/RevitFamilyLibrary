using Autodesk.Revit.UI;
using Family_Library.Revit.ExternalEvents;
using Family_Library.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Family_Library.UI
{
    public partial class ImportWindow : Window
    {
        private readonly ImportViewModel _vm;

        public ImportWindow(UIApplication uiapp, string libraryRoot)
        {
            InitializeComponent();
            new ThemeManager(this, ThemeManager.RevitIsDark()).ApplyTheme();

            ExternalEventBridge.EnsureCreated();
            _vm = new ImportViewModel(uiapp, libraryRoot);
            // Close when everything was imported; stay open after a failure so the user can retry.
            _vm.ImportCompleted += (s, e) => { if (!_vm.StatusIsError) Close(); };
            DataContext = _vm;

            Loaded += (s, e) => _vm.BeginCollect();
            PreviewKeyDown += OnPreviewKeyDown;
            // Esc never reaches Revit; it closes an open drop-down, else clears the search, else the window.
            EscapeGuard.Attach(this, HandleEscapeKey);
        }

        public ImportViewModel ViewModel => _vm;

        private void HandleEscapeKey()
        {
            if (Keyboard.FocusedElement is DependencyObject d)
            {
                while (d != null && !(d is ComboBox))
                    d = System.Windows.Media.VisualTreeHelper.GetParent(d);
                if (d is ComboBox combo && combo.IsDropDownOpen)
                {
                    combo.IsDropDownOpen = false;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_vm.SearchText))
            {
                _vm.SearchText = "";
                return;
            }

            if (!_vm.IsBusy)
                Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
