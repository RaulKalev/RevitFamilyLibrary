using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Family_Library.Revit.ExternalEvents;
using Family_Library.Services;
using Family_Library.UI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace Family_Library.UI.ViewModels
{
    public class MainWindowViewModel
    {
        private static ObservableCollection<string> LoadTypeThumbs(string libraryRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || string.IsNullOrWhiteSpace(relativePath))
                return new ObservableCollection<string>();

            // IMPORTANT: RelativePath in JSON uses "/" so normalize for windows folders
            var rel = relativePath.Replace('/', Path.DirectorySeparatorChar);

            var relDir = Path.GetDirectoryName(rel) ?? "";
            var familyNameNoExt = Path.GetFileNameWithoutExtension(rel);

            var folder = Path.Combine(libraryRoot, "Thumbs_Types", relDir, familyNameNoExt);
            if (!Directory.Exists(folder))
                return new ObservableCollection<string>();

            var files = Directory.GetFiles(folder, "*.png")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ObservableCollection<string>(files);
        }

        private List<LibraryItem> _allItems = new List<LibraryItem>();

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? "";
                ApplyFilters();
            }
        }

        private string _selectedFilterCategory = "All";
        public string SelectedFilterCategory
        {
            get => _selectedFilterCategory;
            set
            {
                _selectedFilterCategory = string.IsNullOrWhiteSpace(value) ? "All" : value;
                ApplyFilters();
            }
        }

        private readonly UIApplication _uiapp;

        public ObservableCollection<LibraryItem> Items { get; } = new ObservableCollection<LibraryItem>();
        public ObservableCollection<string> UserCategories { get; } = new ObservableCollection<string>();

        public string NewUserCategory { get; set; } = "";

        public LibraryItem SelectedItem { get; set; }
        public List<LibraryItem> SelectedItems { get; set; } = new List<LibraryItem>();

        public bool PlaceAfterLoading { get; set; } = false;

        public string LibraryRoot
        {
            get => SettingsStore.Load().LibraryRoot;
            set
            {
                var s = SettingsStore.Load();
                s.LibraryRoot = value;
                SettingsStore.Save(s);
            }
        }

        public int ThumbnailPixelSize
        {
            get => SettingsStore.Load().ThumbnailPixelSize;
            set
            {
                var s = SettingsStore.Load();
                s.ThumbnailPixelSize = value;
                SettingsStore.Save(s);
            }
        }

        public ICommand BrowseLibraryRootCommand => new ricaun.Revit.Mvvm.RelayCommand(BrowseRoot);
        public ICommand RefreshCommand => new ricaun.Revit.Mvvm.RelayCommand(Refresh);
        public ICommand BuildIndexCommand => new ricaun.Revit.Mvvm.RelayCommand(RunBuildIndex);
        public ICommand GenerateThumbsAndIndexCommand => new ricaun.Revit.Mvvm.RelayCommand(RunGenerate);
        public ICommand LoadSelectedCommand => new ricaun.Revit.Mvvm.RelayCommand(RunLoadSelected);
        public ICommand AddUserCategoryCommand => new ricaun.Revit.Mvvm.RelayCommand(AddUserCategory);
        public ICommand RemoveUserCategoryCommand => new ricaun.Revit.Mvvm.RelayCommand(RemoveSelectedUserCategory);

        public MainWindowViewModel(UIApplication uiapp)
        {
            _uiapp = uiapp;

            LoadUserCategories();
            Refresh();
        }

        private void LoadUserCategories()
        {
            UserCategories.Clear();
            UserCategories.Add("All");

            var s = SettingsStore.Load();
            if (s.UserCategories == null) s.UserCategories = new List<string>();

            foreach (var c in s.UserCategories
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x))
            {
                UserCategories.Add(c);
            }

            if (string.IsNullOrWhiteSpace(SelectedFilterCategory))
                SelectedFilterCategory = "All";
        }
        private void SaveUserCategories()
        {
            var s = SettingsStore.Load();
            s.UserCategories = UserCategories
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(x => !string.Equals(x, "All", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            SettingsStore.Save(s);
        }

        public string SelectedUserCategory { get; set; } = "";

        private void AddUserCategory()
        {
            var c = (NewUserCategory ?? "").Trim();
            if (string.IsNullOrWhiteSpace(c))
                return;

            if (!UserCategories.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)))
                UserCategories.Add(c);

            NewUserCategory = "";
            SaveUserCategories();
        }

        private void RemoveSelectedUserCategory()
        {
            var c = (SelectedUserCategory ?? "").Trim();
            if (string.IsNullOrWhiteSpace(c))
                return;

            var existing = UserCategories.FirstOrDefault(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                UserCategories.Remove(existing);

            SaveUserCategories();
        }
        public void SaveIndex()
        {
            var root = LibraryRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            var indexPath = Path.Combine(root, "index.json");
            IndexStore.Write(indexPath, _allItems.ToList());
        }

        private void BrowseRoot()
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "Select Family Library root folder";
                dlg.SelectedPath = Directory.Exists(LibraryRoot)
                    ? LibraryRoot
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (dlg.ShowDialog() != WinForms.DialogResult.OK)
                    return;

                LibraryRoot = dlg.SelectedPath;
                Refresh();
            }
        }
        private void Refresh()
        {
            Items.Clear();
            _allItems.Clear();

            var root = LibraryRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            var indexPath = Path.Combine(root, "index.json");
            if (!File.Exists(indexPath))
                return;

            var list = IndexStore.Read(indexPath) ?? new List<LibraryItem>();

            // Keep full list in memory
            _allItems = list;
            // Populate per-type thumbnail gallery (computed from disk)
            foreach (var it in _allItems)
            {
                it.TypeThumbnailPaths = LoadTypeThumbs(root, it.RelativePath);
                it.SelectedThumbnailIndex = 0;

                // Optional: if you want family thumb to be first in gallery
                // (so arrows appear even when only 1 type image exists)
                // if (!string.IsNullOrWhiteSpace(it.ThumbnailPath) && File.Exists(it.ThumbnailPath))
                // {
                //     if (it.TypeThumbnailPaths == null) it.TypeThumbnailPaths = new ObservableCollection<string>();
                //     if (!it.TypeThumbnailPaths.Contains(it.ThumbnailPath, StringComparer.OrdinalIgnoreCase))
                //         it.TypeThumbnailPaths.Insert(0, it.ThumbnailPath);
                // }
            }

            var doc = _uiapp?.ActiveUIDocument?.Document;

            var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (doc != null)
            {
                loadedNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Family))
                    .Cast<Autodesk.Revit.DB.Family>()
                    .Select(f => f?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var it in _allItems)
            {
                // simplest + reliable: compare to RFA filename (your DisplayName)
                it.IsLoadedInProject = !string.IsNullOrWhiteSpace(it.DisplayName)
                                       && loadedNames.Contains(it.DisplayName);
            }

            // Apply current search/category filters
            ApplyFilters();
        }
        private void ApplyFilters()
        {
            Items.Clear();

            IEnumerable<LibraryItem> q = _allItems ?? Enumerable.Empty<LibraryItem>();

            // Category filter (match if item contains selected category)
            if (!string.IsNullOrWhiteSpace(SelectedFilterCategory) &&
                !string.Equals(SelectedFilterCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(x =>
                    x.UserCategories != null &&
                    x.UserCategories.Any(c => string.Equals(c, SelectedFilterCategory, StringComparison.OrdinalIgnoreCase)));
            }


            // Search filter
            var s = (SearchText ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
                q = q.Where(x =>
                    Contains(x.DisplayName, s) ||
                    Contains(x.Category, s) ||
                    Contains(x.RelativePath, s) ||
                    (x.UserCategories != null && x.UserCategories.Any(c => Contains(c, s))) ||
                    (x.TypeNames != null && x.TypeNames.Any(t => Contains(t, s)))
                );
            }

            foreach (var it in q)
                Items.Add(it);
        }

        private static bool Contains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return false;

            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RunBuildIndex()
        {
            if (!Directory.Exists(LibraryRoot))
                return;

            ExternalEventBridge.Handler.Request.LibraryRoot = LibraryRoot;
            ExternalEventBridge.Handler.Request.TaskType = LibraryTaskType.BuildIndex;
            ExternalEventBridge.Event.Raise();
        }

        private void RunGenerate()
        {
            if (!Directory.Exists(LibraryRoot))
                return;

            ExternalEventBridge.Handler.Request.LibraryRoot = LibraryRoot;
            ExternalEventBridge.Handler.Request.ThumbnailPixelSize = ThumbnailPixelSize;
            ExternalEventBridge.Handler.Request.TaskType = LibraryTaskType.GenerateThumbnailsAndIndex;
            ExternalEventBridge.Event.Raise();
        }

        private void RunLoadSelected()
        {
            var items = (SelectedItems != null && SelectedItems.Count > 0)
                ? SelectedItems
                : (SelectedItem != null ? new System.Collections.Generic.List<LibraryItem> { SelectedItem } : null);

            if (items == null || items.Count == 0)
                return;

            var paths = items
                .Select(x => x?.FullPath)
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
                return;

            ExternalEventBridge.Handler.Request.SelectedFamilyPaths = paths;
            ExternalEventBridge.Handler.Request.PlaceAfterLoading = PlaceAfterLoading;
            ExternalEventBridge.Handler.Request.TaskType = LibraryTaskType.LoadSelectedFamilies;
            ExternalEventBridge.Event.Raise();
        }
        public void EnsureUserCategoryExists(string category)
        {
            category = (category ?? "").Trim();
            if (string.IsNullOrWhiteSpace(category) || string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
                return;

            if (!UserCategories.Any(x => string.Equals(x, category, StringComparison.OrdinalIgnoreCase)))
                UserCategories.Add(category);

            // Persist to settings (excluding "All")
            SaveUserCategories();
        }

    }
}
