using Autodesk.Revit.UI;
using Family_Library.Revit.ExternalEvents;
using Family_Library.UI.Models;
using Family_Library.UI.ViewModels;
using MaterialDesignThemes.Wpf;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace Family_Library.UI
{
    public partial class MainWindow : Window
    {
        /// <summary>Below this width the sidebar collapses to icons.</summary>
        private const double CompactWidth = 900;

        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
            nameof(IsCompact), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        private readonly ThemeManager _theme;
        private readonly MainWindowViewModel _vm;

        public MainWindow(UIApplication uiapp)
        {
            InitializeComponent();

            _theme = new ThemeManager(this, ThemeManager.RevitIsDark());
            _theme.ApplyTheme();
            _theme.ThemeChanged += (s, e) => UpdateThemeButton();
            UpdateThemeButton();
            RestorePlacement();

            ExternalEventBridge.EnsureCreated();
            _vm = new MainWindowViewModel(uiapp);
            _vm.CurrentPage = _theme.Preferences.LastPage;
            DataContext = _vm;

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            SourceInitialized += MainWindow_SourceInitialized;
            SizeChanged += (s, e) => UpdateLayoutMode();
            StateChanged += MainWindow_StateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            EscapeGuard.Attach(this, HandleEscapeKey);
        }

        // ------------------------------------------------------------------ Escape and minimize (as in CAD Manager)
        //
        // Escape never reaches Revit (EscapeGuard). Minimize requests that did not come from our own minimize button
        // or the placement flow are refused, because Revit minimizes modeless windows on some keys.

        private const int WmSysCommand = 0x0112;
        private const int ScMinimize = 0xF020;

        private HwndSource _windowSource;
        private bool _allowExplicitMinimize;

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            _windowSource?.AddHook(WindowHwndHook);
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _windowSource?.RemoveHook(WindowHwndHook);
            _windowSource = null;
        }

        private IntPtr WindowHwndHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMinimize && !_allowExplicitMinimize)
                handled = true;
            return IntPtr.Zero;
        }

        /// <summary>
        /// Esc steps back one level: close an open drop-down or menu, else clear the search while typing in it,
        /// else clear the selection. It never minimizes or closes the window.
        /// </summary>
        private void HandleEscapeKey()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;

            var combo = FindAncestor<ComboBox>(focused);
            if (combo != null && combo.IsDropDownOpen)
            {
                combo.IsDropDownOpen = false;
                return;
            }

            var menu = FindAncestor<ContextMenu>(focused);
            if (menu != null && menu.IsOpen)
            {
                menu.IsOpen = false;
                return;
            }

            if (!_vm.IsLibraryPage)
                return;

            if (SearchTextBox.IsKeyboardFocusWithin && !string.IsNullOrEmpty(SearchTextBox.Text))
                SearchTextBox.Text = string.Empty;
            else
                LibraryList.UnselectAll();
        }

        /// <summary>Minimizes on purpose (placement after loading); other minimize requests are refused.</summary>
        public void MinimizeForPlacement()
        {
            _allowExplicitMinimize = true;
            Topmost = false;
            WindowState = WindowState.Minimized;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && !_allowExplicitMinimize)
            {
                // Minimized by something else (Revit reacting to a key): bring it straight back.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (WindowState == WindowState.Minimized)
                        SystemCommands.RestoreWindow(this);
                    Activate();
                }), DispatcherPriority.Send);
                return;
            }

            if (WindowState != WindowState.Minimized)
                _allowExplicitMinimize = false;

            UpdateMaximizedState();
        }

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            private set => SetValue(IsCompactProperty, value);
        }

        // ------------------------------------------------------------------ appearance and window

        private void UpdateThemeButton()
        {
            // Same icon pair as the other RK Tools plugins (Sentinel, CableCatalogue).
            ThemeIcon.Kind = IconKind(_theme.IsDarkMode ? "WeatherNight" : "WhiteBalanceSunny");
            ThemeText.Text = _theme.IsDarkMode ? "Tume välimus" : "Hele välimus";
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e) => _theme.ToggleTheme();

        private void RestorePlacement()
        {
            var p = _theme.Preferences;
            Width = Math.Max(MinWidth, p.WindowWidth);
            Height = Math.Max(MinHeight, p.WindowHeight);
            if (double.IsNaN(p.WindowLeft) || double.IsNaN(p.WindowTop))
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }
            Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width, p.WindowLeft));
            Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height, p.WindowTop));
        }

        private void UpdateLayoutMode()
        {
            IsCompact = ActualWidth < CompactWidth;
            SidebarColumn.Width = new GridLength(IsCompact ? 60 : 200);
            PageSubtitleText.Visibility = ActualWidth < 1000 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateMaximizedState()
        {
            // A maximized WindowChrome window extends past the work area by the resize frame; pad the content back in.
            var maximized = WindowState == WindowState.Maximized;
            var frame = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = maximized ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4) : new Thickness(0);
            MaximizeIcon.Kind = IconKind(maximized ? "WindowRestore" : "WindowMaximize");
            MaximizeButton.ToolTip = maximized ? "Taasta" : "Maksimeeri";
            System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Taasta" : "Maksimeeri");
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            _allowExplicitMinimize = true;
            SystemCommands.MinimizeWindow(this);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _theme.CaptureWindowPlacement();
            _theme.Preferences.LastPage = _vm.CurrentPage;
            _theme.Save();

            _vm.SaveIndex();
            _vm.Dispose();
        }

        // ------------------------------------------------------------------ keyboard

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;

            if (mods == ModifierKeys.Control && (e.Key == Key.D1 || e.Key == Key.NumPad1))
            {
                _vm.CurrentPage = "Library";
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && (e.Key == Key.D2 || e.Key == Key.NumPad2))
            {
                _vm.CurrentPage = "Settings";
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.F)
            {
                _vm.CurrentPage = "Library";
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
            }
        }

        // ------------------------------------------------------------------ library list

        private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.SetSelection(LibraryList.SelectedItems.Cast<LibraryItem>().ToList());
        }

        private void LibraryList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !_vm.HasSelection) return;
            _vm.LoadSelectedCommand.Execute(null);
            e.Handled = true;
        }

        /// <summary>Double-click on a row loads that family (the selection is already the clicked row).</summary>
        private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var source = e.OriginalSource as DependencyObject;
            // Ignore double-clicks on the thumbnail arrows and on the scrollbar.
            if (FindAncestor<Button>(source) != null || FindAncestor<ListViewItem>(source) == null) return;
            if (!_vm.HasSelection) return;
            _vm.LoadSelectedCommand.Execute(null);
            e.Handled = true;
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e) => LibraryList.UnselectAll();

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }

        private void ListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(sender as DependencyObject);
            if (sv == null) return;

            // Smaller step = smoother.
            const double factor = 0.35;

            var newOffset = sv.VerticalOffset - (e.Delta * factor);
            if (newOffset < 0) newOffset = 0;
            if (newOffset > sv.ScrollableHeight) newOffset = sv.ScrollableHeight;

            sv.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        private void PrevThumb_Click(object sender, RoutedEventArgs e) => ((sender as Button)?.DataContext as LibraryItem)?.PrevThumbnail();

        private void NextThumb_Click(object sender, RoutedEventArgs e) => ((sender as Button)?.DataContext as LibraryItem)?.NextThumbnail();

        // ------------------------------------------------------------------ settings

        private void AddCategory_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            // Push the typed text to the view model first (binding updates on PropertyChanged, but be explicit).
            (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _vm.AddUserCategoryCommand.Execute(null);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Resolves an icon by name at runtime. Never use PackIconKind.X constants in code: the enum's numbers differ
        /// between MaterialDesign versions, and inside Revit another plugin may have loaded a different version first
        /// (Sentinel and CableCatalogue use 5.2.0), which turns e.g. WeatherNight into a rain cloud. XAML is parsed by
        /// name, so it is unaffected.
        /// </summary>
        private static PackIconKind IconKind(string name) => (PackIconKind)Enum.Parse(typeof(PackIconKind), name);

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;

                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
