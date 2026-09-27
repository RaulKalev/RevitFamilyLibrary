using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Family_Library.UI.Models
{
    public class ProjectFamilyItem : INotifyPropertyChanged
    {
        public string UniqueId { get; set; } = "";
        public string FamilyName { get; set; } = "";
        public string Category { get; set; } = "";
        public int TypeCount { get; set; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        private string _targetFolder = "";
        public string TargetFolder
        {
            get => _targetFolder;
            set { _targetFolder = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
