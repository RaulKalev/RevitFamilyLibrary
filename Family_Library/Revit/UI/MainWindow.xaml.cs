using Autodesk.Revit.UI;
using Family_Library.Revit.ExternalEvents;
using Family_Library.UI.Models;
using Family_Library.UI.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System;
using System.Collections.Generic;
using System.Windows.Input;


namespace Family_Library.UI
{
    public partial class MainWindow : Window
    {
        private readonly WindowResizer _windowResizer;

        public bool PlaceAfterLoading { get; set; } = false;

        public MainWindow(UIApplication uiapp)
        {
            InitializeComponent();

            _windowResizer = new WindowResizer(this);
            MouseMove += Window_MouseMove;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;

            ExternalEventBridge.EnsureCreated();
            DataContext = new MainWindowViewModel(uiapp);
        }

        private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            var lv = sender as ListView;
            if (lv == null) return;

            vm.SelectedItems = lv.SelectedItems.Cast<LibraryItem>().ToList();
        }
        private void UserCategoryCombo_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            var cb = sender as System.Windows.Controls.ComboBox;
            if (cb == null) return;

            var typed = (cb.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(typed))
            {
                // add to global list if missing
                if (!vm.UserCategories.Any(x => string.Equals(x, typed, StringComparison.OrdinalIgnoreCase)))
                {
                    vm.UserCategories.Add(typed);
                    // persist list
                    var s = Services.SettingsStore.Load();
                    if (s.UserCategories == null) s.UserCategories = new System.Collections.Generic.List<string>();
                    s.UserCategories = vm.UserCategories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                    Services.SettingsStore.Save(s);
                }
            }

            // persist per-family selection back to index.json
            vm.SaveIndex();
        }
        private void UserCategoriesText_LostFocus(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;

            // After user edits a row, update global category list from ALL items
            var allCats = vm.Items
                .SelectMany(x => x.UserCategories ?? new System.Collections.Generic.List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            // Ensure "All" stays at top
            vm.UserCategories.Clear();
            vm.UserCategories.Add("All");
            foreach (var c in allCats)
                vm.UserCategories.Add(c);

            // Persist index (IMPORTANT: save full list, not filtered list)
            vm.SaveIndex();
        }
        private void TagInput_KeyDown(object sender, KeyEventArgs e)
        {
            // Add on Enter or Comma
            if (e.Key != Key.Enter && e.Key != Key.OemComma)
                return;

            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;

            var item = tb.DataContext as LibraryItem;
            if (item == null) return;

            var raw = (tb.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return;

            // Allow pasting multiple tags: "A, B; C"
            var tags = raw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tags.Count == 0) return;

            if (item.UserCategories == null)
                item.UserCategories = new System.Collections.Generic.List<string>();

            foreach (var tag in tags)
            {
                if (!item.UserCategories.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
                    item.UserCategories.Add(tag);
            }

            tb.Text = "";
            e.Handled = true;

            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            // Add new tags to global category list (settings list)
            foreach (var tag in tags)
                vm.EnsureUserCategoryExists(tag);

            // Save the index safely (must save FULL list, not filtered Items)
            vm.SaveIndex();
        }
        private void AddCategory_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            var vm = DataContext as MainWindowViewModel;
            vm?.AddUserCategoryCommand?.Execute(null);
            e.Handled = true;
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox == null) return;
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }
        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeWE;
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeWE;
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNS;
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNESW;
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNWSE;

        private void Edge_MouseLeave(object sender, MouseEventArgs e) => Cursor = Cursors.Arrow;

        private void Window_MouseMove(object sender, MouseEventArgs e) => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowResizer.StopResizing();

        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomRight);


    }
}
