using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Family_Library.UI.Models
{
    public class LibraryItem : INotifyPropertyChanged
    {
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string RelativePath { get; set; } = "";

        // Not serialized - computed at runtime from RelativePath + LibraryRoot
        [JsonIgnore]
        public string FullPath { get; set; } = "";

        // Main "family" thumbnail (fallback) - computed at runtime
        [JsonIgnore]
        public string ThumbnailPath { get; set; } = "";

        public List<string> TypeNames { get; set; } = new List<string>();

        // For display + incremental indexing
        public string SavedInRevitVersion { get; set; } = "";
        public DateTime LastWriteTimeUtc { get; set; } = default;

        // Computed property - not stored
        [JsonIgnore]
        public string LastModifiedLocal =>
            LastWriteTimeUtc == default ? "" : LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        // Runtime state - not stored
        [JsonIgnore]
        public bool IsLoadedInProject { get; set; } = false;

        public ObservableCollection<string> UserCategories { get; set; } = new ObservableCollection<string>();

        // Per-type gallery thumbnails - computed at runtime from disk
        private ObservableCollection<string> _typeThumbnailPaths = new ObservableCollection<string>();
        [JsonIgnore]
        public ObservableCollection<string> TypeThumbnailPaths
        {
            get => _typeThumbnailPaths;
            set
            {
                _typeThumbnailPaths = value ?? new ObservableCollection<string>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentThumbnailPath));
                OnPropertyChanged(nameof(HasMultipleThumbnails));
                OnPropertyChanged(nameof(CanPrevThumbnail));
                OnPropertyChanged(nameof(CanNextThumbnail));
            }
        }

        private int _selectedThumbnailIndex = 0;
        [JsonIgnore]
        public int SelectedThumbnailIndex
        {
            get => _selectedThumbnailIndex;
            set
            {
                var max = (TypeThumbnailPaths?.Count ?? 0) - 1;
                if (max < 0) max = 0;

                if (value < 0) value = 0;
                if (value > max) value = max;

                if (_selectedThumbnailIndex == value) return;

                _selectedThumbnailIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentThumbnailPath));
                OnPropertyChanged(nameof(CanPrevThumbnail));
                OnPropertyChanged(nameof(CanNextThumbnail));
                OnPropertyChanged(nameof(HasMultipleThumbnails));
            }
        }

        [JsonIgnore]
        public string CurrentThumbnailPath
        {
            get
            {
                if (TypeThumbnailPaths != null && TypeThumbnailPaths.Count > 0)
                {
                    var idx = Math.Max(0, Math.Min(SelectedThumbnailIndex, TypeThumbnailPaths.Count - 1));
                    return TypeThumbnailPaths[idx];
                }
                return ThumbnailPath;
            }
        }

        [JsonIgnore]
        public bool HasMultipleThumbnails => (TypeThumbnailPaths?.Count ?? 0) > 1;
        [JsonIgnore]
        public bool CanPrevThumbnail => HasMultipleThumbnails && SelectedThumbnailIndex > 0;
        [JsonIgnore]
        public bool CanNextThumbnail => HasMultipleThumbnails && SelectedThumbnailIndex < (TypeThumbnailPaths.Count - 1);

        public void PrevThumbnail()
        {
            if (CanPrevThumbnail) SelectedThumbnailIndex--;
        }

        public void NextThumbnail()
        {
            if (CanNextThumbnail) SelectedThumbnailIndex++;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
