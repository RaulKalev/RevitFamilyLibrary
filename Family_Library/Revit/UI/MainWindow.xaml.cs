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
using System.Collections.ObjectModel;
using System.Collections.Specialized;


namespace Family_Library.UI
{
    public partial class MainWindow : Window
    {
        private bool _hooksInitialized = false;

        private readonly WindowResizer _windowResizer;

        public bool PlaceAfterLoading { get; set; } = false;

        public MainWindow(UIApplication uiapp)
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
            InitCategoryChangeHooks();

            _windowResizer = new WindowResizer(this);
            MouseMove += Window_MouseMove;
            MouseLeftButtonUp += Window_MouseLeftButtonUp;

            ExternalEventBridge.EnsureCreated();
            DataContext = new MainWindowViewModel(uiapp);
        }
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            vm?.SaveIndex();
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
                .SelectMany(x => x.UserCategories ?? new ObservableCollection<string>())
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
                item.UserCategories = new ObservableCollection<string>();

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

        private void UserCategories_ItemSelectionChanged(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            // Persist per-family selection back to index.json (full list, not filtered)
            vm.SaveIndex();

            // If you are currently filtering by category, a change can affect visibility
            // Easiest: re-apply filters by re-setting SearchText/SelectedFilterCategory indirectly,
            // or add a public vm.RefreshFilteredView() method that calls ApplyFilters().
            // Minimal “no new public methods” hack:
            vm.SearchText = vm.SearchText; // triggers ApplyFilters() via setter
        }
        private void InitCategoryChangeHooks()
        {
            if (_hooksInitialized) return;
            _hooksInitialized = true;

            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            // Hook existing items
            foreach (var it in vm.Items)
                HookItem(it);

            // Hook new items if list changes
            vm.Items.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LibraryItem it in e.NewItems)
                        HookItem(it);

                if (e.OldItems != null)
                    foreach (LibraryItem it in e.OldItems)
                        UnhookItem(it);
            };
        }

        private void HookItem(LibraryItem item)
        {
            if (item == null) return;

            // Ensure not null
            if (item.UserCategories == null)
                item.UserCategories = new ObservableCollection<string>();

            // Avoid double-hook
            item.UserCategories.CollectionChanged -= ItemUserCategories_CollectionChanged;
            item.UserCategories.CollectionChanged += ItemUserCategories_CollectionChanged;
        }

        private void UnhookItem(LibraryItem item)
        {
            if (item?.UserCategories == null) return;
            item.UserCategories.CollectionChanged -= ItemUserCategories_CollectionChanged;
        }

        private void ItemUserCategories_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (vm == null) return;

            // 1) Save immediately
            vm.SaveIndex();

            // 2) Re-apply filters immediately (your hack works, but let’s do it safely)
            vm.SearchText = vm.SearchText;
        }
        private void ListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(sender as DependencyObject);
            if (sv == null) return;

            // Smaller step = smoother. Tune this number.
            const double factor = 0.35;

            var newOffset = sv.VerticalOffset - (e.Delta * factor);
            if (newOffset < 0) newOffset = 0;
            if (newOffset > sv.ScrollableHeight) newOffset = sv.ScrollableHeight;

            sv.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;

                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
        private void PrevThumb_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.DataContext as LibraryItem;
            item?.PrevThumbnail();
        }

        private void NextThumb_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.DataContext as LibraryItem;
            item?.NextThumbnail();
        }

    }
}
