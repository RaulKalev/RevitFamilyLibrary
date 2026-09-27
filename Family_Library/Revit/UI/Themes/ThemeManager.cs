using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace Family_Library.UI
{
    /// <summary>Per-user UI preferences (appearance, window placement, last page).</summary>
    public class UiPreferences
    {
        /// <summary>Null until the user picks one; then the Revit theme is no longer followed.</summary>
        public bool? IsDarkMode { get; set; }
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 760;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public string LastPage { get; set; } = "Library";
    }

    /// <summary>
    /// Applies the appearance to a window, as in RK Tools Sentinel: Dark/Light palette plus the Windows accessibility
    /// preferences (high contrast → system colours, transparency off → solid materials, animations off →
    /// <see cref="ReducedMotion"/>). Only the palette dictionaries are swapped; styles use DynamicResource.
    /// Preferences persist to %LocalAppData%\RK Tools\Family_Library\ui.json.
    /// </summary>
    public class ThemeManager
    {
        private static readonly string PrefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RK Tools", "Family_Library", "ui.json");

        private readonly Window _window;
        private readonly bool _defaultDark;
        private ResourceDictionary[] _applied = new ResourceDictionary[0];

        public UiPreferences Preferences { get; private set; } = new UiPreferences();
        public bool IsDarkMode => Preferences.IsDarkMode ?? _defaultDark;

        public event EventHandler ThemeChanged;

        /// <param name="defaultDark">Appearance used until the user chooses one (the current Revit theme).</param>
        public ThemeManager(Window window, bool defaultDark)
        {
            _window = window;
            _defaultDark = defaultDark;
            Load();
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            window.Closed += (s, e) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        }

        /// <summary>Revit's current theme (the default until the user picks one). UIThemeManager exists from Revit 2024.</summary>
        public static bool RevitIsDark()
        {
            try { return ReadRevitTheme(); }
            catch { return true; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool ReadRevitTheme() =>
            Autodesk.Revit.UI.UIThemeManager.CurrentTheme == Autodesk.Revit.UI.UITheme.Dark;

        public static bool HighContrast => SystemParameters.HighContrast;

        /// <summary>Windows "Transparency effects" switched off.</summary>
        public static bool SolidMaterials
        {
            get
            {
                if (SystemParameters.HighContrast) return true;
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        var v = key?.GetValue("EnableTransparency");
                        return v is int i && i == 0;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Windows "Animation effects" switched off.</summary>
        public static bool ReducedMotion => !SystemParameters.ClientAreaAnimation;

        private void OnSystemParametersChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast) || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyTheme();
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }));
        }

        public void ToggleTheme()
        {
            Preferences.IsDarkMode = !IsDarkMode;
            ApplyTheme();
            Save();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyTheme()
        {
            var target = _window.Resources;
            var dicts = BuildDictionaries();

            foreach (var d in _applied) target.MergedDictionaries.Remove(d);
            // Palette merged from XAML at parse time (for the designer) is replaced too.
            foreach (var d in target.MergedDictionaries.Where(IsPaletteSource).ToList()) target.MergedDictionaries.Remove(d);

            for (int i = 0; i < dicts.Length; i++) target.MergedDictionaries.Insert(i, dicts[i]);
            _applied = dicts;
        }

        private static bool IsPaletteSource(ResourceDictionary d)
        {
            var s = d.Source?.OriginalString;
            return s != null && (s.EndsWith("/DarkTheme.xaml") || s.EndsWith("/LightTheme.xaml"));
        }

        private ResourceDictionary[] BuildDictionaries()
        {
            if (HighContrast) return new[] { BuildHighContrast() };

            var palette = new ResourceDictionary
            {
                Source = new Uri(IsDarkMode
                    ? "pack://application:,,,/Family_Library;component/Revit/UI/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/Family_Library;component/Revit/UI/Themes/LightTheme.xaml", UriKind.Absolute)
            };
            var materials = SolidMaterials
                ? BuildSolidMaterials(palette)
                : new ResourceDictionary { ["Shadow.Opacity"] = IsDarkMode ? 0.45 : 0.16 };
            return new[] { palette, materials };
        }

        /// <summary>Reduced transparency: materials become solid surfaces with a defined edge, no shadows.</summary>
        private static ResourceDictionary BuildSolidMaterials(ResourceDictionary palette) => new ResourceDictionary
        {
            ["Sidebar.Material"] = palette["Surface.Inset"],
            ["Floating.Material"] = palette["Surface.Raised"],
            ["Floating.Edge"] = palette["Surface.Stroke"],
            ["Sidebar.Edge"] = palette["Surface.Stroke"],
            ["Shadow.Opacity"] = 0.0
        };

        /// <summary>High contrast: every brush maps to a Windows system colour so the user's scheme is honoured.</summary>
        private static ResourceDictionary BuildHighContrast()
        {
            Func<Color, SolidColorBrush> b = c => { var br = new SolidColorBrush(c); br.Freeze(); return br; };
            var window = b(SystemColors.WindowColor);
            var text = b(SystemColors.WindowTextColor);
            var highlight = b(SystemColors.HighlightColor);
            var hot = b(SystemColors.HotTrackColor);
            var btn = b(SystemColors.ControlColor);
            var d = new ResourceDictionary();
            foreach (var k in new[] { "Window.Background", "Sidebar.Material", "Floating.Material", "Surface.Content", "Surface.Raised",
                                      "Surface.Inset", "Thumb.Background", "Input.Fill", "Control.Fill", "Status.Ok.Subtle",
                                      "Status.Warning.Subtle", "Status.Neutral.Subtle", "Accent.Subtle" })
                d[k] = window;
            foreach (var k in new[] { "Window.Stroke", "Sidebar.Edge", "Floating.Edge", "Surface.Stroke", "Separator", "Control.Stroke",
                                      "Input.Stroke", "Input.StrokeHover", "Text.Primary", "Text.Secondary", "Text.Tertiary",
                                      "Status.Ok", "Status.Warning", "Status.Error" })
                d[k] = text;
            d["Text.Disabled"] = b(SystemColors.GrayTextColor);
            foreach (var k in new[] { "Accent", "Accent.Hover", "Accent.Pressed", "Row.Selected", "Row.SelectedInactive", "Control.FillPressed" })
                d[k] = highlight;
            d["Text.OnAccent"] = b(SystemColors.HighlightTextColor);
            d["Accent.Text"] = hot;
            d["Focus.Ring"] = hot;
            d["Control.FillHover"] = btn;
            d["Control.Plain.Hover"] = btn;
            d["Control.Plain.Pressed"] = highlight;
            d["Row.Hover"] = btn;
            d["Shadow.Color"] = Colors.Black;
            d["Shadow.Opacity"] = 0.0;
            return d;
        }

        public void CaptureWindowPlacement()
        {
            if (_window.WindowState != WindowState.Normal) return;
            Preferences.WindowWidth = _window.Width;
            Preferences.WindowHeight = _window.Height;
            Preferences.WindowLeft = _window.Left;
            Preferences.WindowTop = _window.Top;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(PrefsPath)) return;
                Preferences = JsonConvert.DeserializeObject<UiPreferences>(File.ReadAllText(PrefsPath)) ?? new UiPreferences();
            }
            catch
            {
                Preferences = new UiPreferences();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath));
                File.WriteAllText(PrefsPath, JsonConvert.SerializeObject(Preferences, Formatting.Indented));
            }
            catch
            {
                // preferences are best effort
            }
        }
    }
}
