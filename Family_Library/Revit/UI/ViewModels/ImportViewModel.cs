using Autodesk.Revit.UI;
using Family_Library.Revit.ExternalEvents;
using Family_Library.Services;
using Family_Library.UI.Dialogs;
using Family_Library.UI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Family_Library.UI.ViewModels
{
    public class ImportViewModel : INotifyPropertyChanged
    {
        private readonly UIApplication _uiapp;
        private readonly string _libraryRoot;

        // Created on the window's thread. Application.Current can be null inside Revit, so it is not used.
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

        public ObservableCollection<ProjectFamilyItem> Families { get; } = new ObservableCollection<ProjectFamilyItem>();
        public ObservableCollection<string> AvailableFolders { get; } = new ObservableCollection<string>();

        /// <summary>Families filtered by <see cref="SearchText"/> (what the grid shows).</summary>
        public ICollectionView FamiliesView { get; }

        private string _status = "Kogun projekti perekondi…";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private bool _statusIsError;
        public bool StatusIsError
        {
            get => _statusIsError;
            set { _statusIsError = value; OnPropertyChanged(); }
        }

        private bool _isBusy = true;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(CanImport));
                OnPropertyChanged(nameof(ShowEmpty));
            }
        }
        public bool IsNotBusy => !_isBusy;

        private string _batchFolder = "";
        public string BatchFolder
        {
            get => _batchFolder;
            set { _batchFolder = value; OnPropertyChanged(); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? "";
                OnPropertyChanged();
                FamiliesView.Refresh();
            }
        }

        public int SelectedCount => Families.Count(f => f.IsSelected);
        public bool CanImport => !IsBusy && SelectedCount > 0;
        public bool ShowEmpty => !IsBusy && Families.Count == 0;
        public string ImportButtonText => SelectedCount == 1 ? "Impordi 1 perekond" : "Impordi " + SelectedCount + " perekonda";
        public string SummaryText => Families.Count == 0 ? "" : SelectedCount + " / " + Families.Count + " valitud";

        public ICommand SelectAllCommand => new ricaun.Revit.Mvvm.RelayCommand(SelectAll);
        public ICommand DeselectAllCommand => new ricaun.Revit.Mvvm.RelayCommand(DeselectAll);
        public ICommand ApplyBatchFolderCommand => new ricaun.Revit.Mvvm.RelayCommand(ApplyBatchFolder);
        public ICommand ImportCommand => new ricaun.Revit.Mvvm.RelayCommand(RunImport);

        public event EventHandler ImportCompleted;

        public ImportViewModel(UIApplication uiapp, string libraryRoot)
        {
            _uiapp = uiapp;
            _libraryRoot = libraryRoot;

            FamiliesView = CollectionViewSource.GetDefaultView(Families);
            FamiliesView.Filter = o =>
            {
                var s = (SearchText ?? "").Trim();
                if (s.Length == 0) return true;
                var f = (ProjectFamilyItem)o;
                return (f.FamilyName ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    || (f.Category ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
            };

            LoadAvailableFolders();
        }

        public void BeginCollect()
        {
            CollectFamiliesFromProject();
        }

        private void LoadAvailableFolders()
        {
            AvailableFolders.Clear();
            try
            {
                var familiesFolder = LibraryIndexer.GetFamiliesFolder(_libraryRoot);
                if (Directory.Exists(familiesFolder))
                {
                    var dirs = Directory.GetDirectories(familiesFolder, "*", SearchOption.AllDirectories);
                    foreach (var dir in dirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        var rel = dir.Substring(familiesFolder.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                        if (!string.IsNullOrWhiteSpace(rel))
                            AvailableFolders.Add(rel);
                    }
                }
            }
            catch { }
        }

        private void CollectFamiliesFromProject()
        {
            IsBusy = true;
            StatusIsError = false;
            Status = "Kogun projekti perekondi…";

            ExternalEventBridge.Handler.Request.TaskType = LibraryTaskType.CollectProjectFamilies;

            EventHandler handler = null;
            handler = (s, e) =>
            {
                ExternalEventBridge.Handler.OnCompleted -= handler;
                var failed = ExternalEventBridge.Handler.LastRunFailed;
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var f in Families) f.PropertyChanged -= Family_PropertyChanged;
                    Families.Clear();
                    var collected = failed ? null : ExternalEventBridge.Handler.Request.CollectedFamilies;
                    if (collected != null)
                    {
                        foreach (var item in collected)
                        {
                            item.PropertyChanged += Family_PropertyChanged;
                            Families.Add(item);
                        }
                    }

                    StatusIsError = failed;
                    Status = failed
                        ? "Projekti perekondi ei saanud lugeda."
                        : Families.Count > 0 ? Families.Count + " perekonda projektis" : "";
                    IsBusy = false;
                    RaiseSelection();
                }));
            };
            ExternalEventBridge.Handler.OnCompleted += handler;
            ExternalEventBridge.Event.Raise();
        }

        private void Family_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectFamilyItem.IsSelected))
                RaiseSelection();
        }

        private void RaiseSelection()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(ShowEmpty));
            OnPropertyChanged(nameof(ImportButtonText));
            OnPropertyChanged(nameof(SummaryText));
        }

        /// <summary>Select / clear acts on the families the search currently shows.</summary>
        private void SelectAll()
        {
            foreach (ProjectFamilyItem f in FamiliesView) f.IsSelected = true;
        }

        private void DeselectAll()
        {
            foreach (ProjectFamilyItem f in FamiliesView) f.IsSelected = false;
        }

        private void ApplyBatchFolder()
        {
            if (string.IsNullOrWhiteSpace(BatchFolder)) return;
            foreach (var f in Families)
            {
                if (f.IsSelected)
                    f.TargetFolder = BatchFolder;
            }
        }

        private void RunImport()
        {
            var selected = Families.Where(f => f.IsSelected).ToList();
            if (!selected.Any())
                return; // the button is disabled without a selection

            // Check for duplicates on disk
            var familiesFolder = LibraryIndexer.GetFamiliesFolder(_libraryRoot);
            var duplicates = new List<string>();
            foreach (var item in selected)
            {
                var targetDir = string.IsNullOrWhiteSpace(item.TargetFolder)
                    ? familiesFolder
                    : Path.Combine(familiesFolder, item.TargetFolder);
                var targetPath = Path.Combine(targetDir, item.FamilyName + ".rfa");
                if (File.Exists(targetPath))
                    duplicates.Add(item.FamilyName + "  →  " + (string.IsNullOrWhiteSpace(item.TargetFolder) ? "(juurkaust)" : item.TargetFolder));
            }

            if (duplicates.Any())
            {
                var choice = LibraryDialogs.Choose(
                    duplicates.Count == 1 ? "Fail on teegis juba olemas" : duplicates.Count + " faili on teegis juba olemas",
                    "Importimine kirjutab need failid teegis üle.",
                    new List<DialogChoice>
                    {
                        new DialogChoice("Kirjuta üle ja impordi", "Asenda teegis olevad failid projekti versiooniga.")
                    },
                    string.Join("\n", duplicates));
                if (choice != 0)
                    return;
            }

            IsBusy = true;
            StatusIsError = false;
            Status = selected.Count == 1 ? "Impordin 1 perekonna…" : "Impordin " + selected.Count + " perekonda…";

            var req = ExternalEventBridge.Handler.Request;
            req.TaskType = LibraryTaskType.ImportFamiliesFromProject;
            req.LibraryRoot = _libraryRoot;
            req.ThumbnailPixelSize = SettingsStore.Load().ThumbnailPixelSize;
            req.ExportedCount = 0;
            req.FamilyExportMap = selected.Select(f => new ProjectFamilyExportEntry
            {
                UniqueId = f.UniqueId,
                TargetFolder = f.TargetFolder
            }).ToList();

            EventHandler handler = null;
            handler = (s, e) =>
            {
                ExternalEventBridge.Handler.OnCompleted -= handler;
                var failed = ExternalEventBridge.Handler.LastRunFailed;
                var exported = ExternalEventBridge.Handler.Request.ExportedCount;
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    IsBusy = false;
                    if (!failed)
                    {
                        Status = exported + " perekonda imporditud";
                        ImportCompleted?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    // The error itself was shown in a dialog; stay open so the user can retry.
                    StatusIsError = true;
                    Status = exported > 0
                        ? exported + " perekonda imporditud, ülejäänud ebaõnnestusid."
                        : "Importimine ebaõnnestus.";
                    if (exported > 0)
                        ImportCompleted?.Invoke(this, EventArgs.Empty);
                }));
            };
            ExternalEventBridge.Handler.OnCompleted += handler;
            ExternalEventBridge.Event.Raise();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
