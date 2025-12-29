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
        // Filtered list for the Main Filter Dropdown
        public ObservableCollection<string> UserCategories { get; } = new ObservableCollection<string>();
        // Full list for the Assignment CheckComboBox
        public ObservableCollection<string> AllUserCategories { get; } = new ObservableCollection<string>();

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

            // Subscribe to completion event to refresh UI
            if (ExternalEventBridge.Handler != null)
            {
                ExternalEventBridge.Handler.OnCompleted += (s, e) =>
                {
                    // Refresh on UI thread
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Refresh();
                    });
                };
            }

            // Initial load
            Refresh();
        }

        private void LoadUserCategories()
        {
            // 1. Populate AllUserCategories (Master List)
            AllUserCategories.Clear();
            var s = SettingsStore.Load();
            var savedCats = s.UserCategories ?? new List<string>();
            
            // Also include categories found in loaded items
            var itemCats = _allItems.SelectMany(x => x.UserCategories ?? Enumerable.Empty<string>());
            
            // ENSURE REQUIRED TAGS ARE PRESENT
            var requiredTags = new[] { "2D", "3D", "EL", "EN", "EA" };
            
            var uniqueCats = savedCats.Concat(itemCats).Concat(requiredTags)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "All", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);

            foreach(var c in uniqueCats)
            {
                AllUserCategories.Add(c);
            }

            // 2. Update the Filtered List based on Toggles
            UpdateVisibleCategories();

            if (string.IsNullOrWhiteSpace(SelectedFilterCategory))
                SelectedFilterCategory = "All";
        }

        private void SaveUserCategories()
        {
            // Save logic works on AllUserCategories really, or rather logic that updates Settings
            // Current Add/Remove logic updates SettingsStore directly or indirectly. 
            // NOTE: SettingsStore.UserCategories usually drives the list.
            var s = SettingsStore.Load();
            
            // We save strictly what is in AllUserCategories? 
            // AddUserCategory adds to AllUserCategories. 
            // So we just dump AllUserCategories to settings.
            s.UserCategories = AllUserCategories.ToList();

            SettingsStore.Save(s);
        }

        public string SelectedUserCategory { get; set; } = "";

        private void AddUserCategory()
        {
            var c = (NewUserCategory ?? "").Trim();
            if (string.IsNullOrWhiteSpace(c))
                return;

            // Check against Master List
            if (!AllUserCategories.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)))
            {
                AllUserCategories.Add(c);
                // Resort? A simple insert or allow unsorted. Let's just Add for now.
                // Or reload entire logic.
            }
                
            NewUserCategory = "";
            SaveUserCategories();
            // Re-run filter logic in case the new category matches current toggle state
            UpdateVisibleCategories();
        }

        private void RemoveSelectedUserCategory()
        {
            var c = (SelectedUserCategory ?? "").Trim();
            if (string.IsNullOrWhiteSpace(c))
                return;

            var existing = AllUserCategories.FirstOrDefault(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                AllUserCategories.Remove(existing);
            }

            SaveUserCategories();
            UpdateVisibleCategories();
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

            // Compute FullPath and ThumbnailPath from RelativePath (for multi-user support)
            var familiesFolder = GetFamiliesFolder(root);
            var thumbsFolder = Path.Combine(root, "Thumbs");
            
            // Cache buster for forcing UI refresh of images
            var ticks = DateTime.Now.Ticks;

            foreach (var it in _allItems)
            {
                if (!string.IsNullOrWhiteSpace(it.RelativePath))
                {
                    var relNormalized = it.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                    it.FullPath = Path.Combine(familiesFolder, relNormalized);
                    var thumbRel = Path.ChangeExtension(relNormalized, ".png");
                    var thumbPath = Path.Combine(thumbsFolder, thumbRel);
                    // Append ticks
                    it.ThumbnailPath = File.Exists(thumbPath) ? thumbPath + "?t=" + ticks : "";
                }
            }

            // Populate per-type thumbnail gallery (computed from disk)
            foreach (var it in _allItems)
            {
                var types = LoadTypeThumbs(root, it.RelativePath);
                // Append ticks to types too
                for (int i = 0; i < types.Count; i++)
                {
                    types[i] = types[i] + "?t=" + ticks;
                }
                it.TypeThumbnailPaths = types;
                it.SelectedThumbnailIndex = 0;
            }

            // Reload Categories (combining Settings + Items)
            LoadUserCategories();

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
        // Defined category sets for toggles
        private readonly HashSet<string> _elCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "Andurid", "Kilbid", "Lülitid", "Pistikud", "Valgusti" 
        };
        private readonly HashSet<string> _enCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "ATS", "Kilbid", "LPS", "SHS", "Side", "VVS" 
        };
        private readonly HashSet<string> _eaCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "Andurid", "Kilbid" 
        };

        // Categories to strictly exclude from the dropdown list
        private readonly HashSet<string> _bannedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EL", "EN", "EA", "2D", "3D"
        };


        public bool IsElChecked
        {
            get => _isElChecked;
            set 
            { 
                if (_isElChecked != value)
                {
                    _isElChecked = value; 
                    SelectedFilterCategory = "All"; // Reset on toggle
                    UpdateVisibleCategories();
                    ApplyFilters();
                }
            }
        }
        private bool _isElChecked;

        public bool IsEnChecked
        {
            get => _isEnChecked;
            set 
            { 
                if (_isEnChecked != value)
                {
                    _isEnChecked = value; 
                    SelectedFilterCategory = "All"; // Reset on toggle
                    UpdateVisibleCategories();
                    ApplyFilters();
                }
            }
        }
        private bool _isEnChecked;

        public bool IsEaChecked
        {
            get => _isEaChecked;
            set 
            { 
                if (_isEaChecked != value)
                {
                    _isEaChecked = value; 
                    SelectedFilterCategory = "All"; // Reset on toggle
                    UpdateVisibleCategories();
                    ApplyFilters();
                }
            }
        }
        private bool _isEaChecked;


        private void UpdateVisibleCategories()
        {
            // 1. Determine which categories are allowed based on toggles
            var allowedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyToggleChecked = IsElChecked || IsEnChecked || IsEaChecked;

            if (anyToggleChecked)
            {
                if (IsElChecked) allowedSet.UnionWith(_elCategories);
                if (IsEnChecked) allowedSet.UnionWith(_enCategories);
                if (IsEaChecked) allowedSet.UnionWith(_eaCategories);
            }
            // If no toggle is checked, we allow everything (except banned)

            // 2. Use AllUserCategories as the source
            // Filter and rebuild UserCategories collection
            var newVisible = new List<string>();
            newVisible.Add("All");

            foreach (var cat in AllUserCategories.OrderBy(x => x))
            {
                if (string.Equals(cat, "All", StringComparison.OrdinalIgnoreCase)) continue;

                // Rule 1: Must not be banned
                if (_bannedCategories.Contains(cat)) continue;

                // Rule 2: If toggles are active, must be in allowed set
                // NEW BEHAVIOR: 
                // The prompt says: "items containg the tags be visible only when corresponding checkbox is toggled"
                // It also says: "when user toggles a checkbox... the kategory in the titlebar category selector is set to all"
                // 
                // But what about the Dropdown List content itself?
                // "Remove specific categories... from the ComboBox category list" (Previous request).
                // So we stick to hiding EL, EN, EA, 2D, 3D from the Main Dropdown.
                // 
                // And we strictly show valid categories for the toggles? 
                // Yes, keep this logic: Only show categories relevant to the active toggles.
                if (anyToggleChecked && !allowedSet.Contains(cat)) continue;

                newVisible.Add(cat);
            }

            // Update ObservableCollection
            UserCategories.Clear();
            foreach (var c in newVisible)
            {
                UserCategories.Add(c);
            }

            // 4. Reset selection if current selection is no longer valid
            if (!UserCategories.Contains(SelectedFilterCategory))
            {
                SelectedFilterCategory = "All";
            }
        }

        public bool Is2DMode
        {
            get => _is2DMode;
            set
            {
                if (_is2DMode != value)
                {
                    _is2DMode = value;
                    ApplyFilters();
                }
            }
        }
        private bool _is2DMode = false; // Default to 3D mode (False)

        private void ApplyFilters()
        {
            Items.Clear();

            IEnumerable<LibraryItem> q = _allItems ?? Enumerable.Empty<LibraryItem>();

            // 0. Base Filter: 2D vs 3D (Always active)
            // Rule:
            // - If Is2DMode is true (2D Mode) -> Show Item IF (Has "2D" OR Does NOT Have "3D")
            // - If Is2DMode is false (3D Mode) -> Show Item IF (Has "3D" OR Does NOT Have "2D")
            // This treats untagged items as existing in BOTH views.
            
            bool is2D = Is2DMode;
            q = q.Where(x => 
            {
                bool has2D = x.UserCategories != null && x.UserCategories.Contains("2D", StringComparer.OrdinalIgnoreCase);
                bool has3D = x.UserCategories != null && x.UserCategories.Contains("3D", StringComparer.OrdinalIgnoreCase);

                if (is2D)
                {
                    // In 2D mode, we want to see 2D items, AND items that are NOT explicitly 3D only.
                    // If something is tagged 3D but NOT 2D, hide it.
                    // If something is tagged 2D, show it.
                    // If something is tagged NEITHER, show it.
                    // So: Show if (Has2D) || (!Has3D)
                    return has2D || !has3D;
                }
                else
                {
                    // In 3D mode (Default), we want to see 3D items, AND items that are NOT explicitly 2D only.
                    // Show if (Has3D) || (!Has2D)
                    return has3D || !has2D;
                }
            });

            // 1. Filter by allowed categories (Toggles) + STRICT TAG CHECKS
            bool anyToggleChecked = IsElChecked || IsEnChecked || IsEaChecked;
            if (anyToggleChecked)
            {
                // Strict Tag Requirement:
                // If EL is checked, item MUST have "EL" tag? 
                // User said: "id like the items containg the tags be visible only when corresponding checkbox is toggled"
                // This implies: Show item IF (IsElChecked AND item.Has("EL")) OR (IsEnChecked AND item.Has("EN")) ...
                
                var activeTags = new List<string>();
                if (IsElChecked) activeTags.Add("EL");
                if (IsEnChecked) activeTags.Add("EN");
                if (IsEaChecked) activeTags.Add("EA");

                // Filter: Item must have AT LEAST ONE of the active tags
                q = q.Where(x => x.UserCategories != null && x.UserCategories.Any(c => activeTags.Contains(c, StringComparer.OrdinalIgnoreCase)));
            }

            // 2. Filter by specific Dropdown selection
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

            if (!AllUserCategories.Any(x => string.Equals(x, category, StringComparison.OrdinalIgnoreCase)))
            {
                AllUserCategories.Add(category);
                SaveUserCategories();
                // Might affect visible criteria
                UpdateVisibleCategories();
            }
        }

        private static string GetFamiliesFolder(string libraryRoot)
        {
            var families = Path.Combine(libraryRoot, "Families");

            // Preferred layout: Families subfolder with RFAs
            if (Directory.Exists(families) &&
                Directory.GetFiles(families, "*.rfa", SearchOption.AllDirectories).Any())
                return families;

            // Legacy layout: RFAs directly under libraryRoot
            bool hasRfasInRoot = Directory.GetFiles(libraryRoot, "*.rfa", SearchOption.AllDirectories)
                .Any(p =>
                {
                    var rel = p.Substring(libraryRoot.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                    return !rel.StartsWith("Thumbs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !rel.StartsWith("Thumbs_Types" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                });

            if (hasRfasInRoot)
                return libraryRoot;

            // Default to preferred layout
            Directory.CreateDirectory(families);
            return families;
        }
        
        public ICommand ToggleCategoryCommand => new ricaun.Revit.Mvvm.RelayCommand<object>(ToggleCategory);
        private void ToggleCategory(object param)
        {
            if (param is object[] arr && arr.Length >= 2)
            {
                var item = arr[0] as LibraryItem;
                var cat = arr[1] as string;
                if (item != null && !string.IsNullOrWhiteSpace(cat))
                {
                   if (item.UserCategories == null) item.UserCategories = new ObservableCollection<string>();
                   
                   var existing = item.UserCategories.FirstOrDefault(x => string.Equals(x, cat, StringComparison.OrdinalIgnoreCase));
                   if (existing != null)
                   {
                       item.UserCategories.Remove(existing);
                   }
                   else
                   {
                       item.UserCategories.Add(cat);
                   }
                   
                   SaveIndex();
                   ApplyFilters();
                }
            }
        }
    }
}
