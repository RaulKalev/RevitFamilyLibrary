using System.Collections.Generic;

namespace Family_Library.UI.Models
{
    public class LibraryItem
    {
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string RelativePath { get; set; }
        public string FullPath { get; set; }
        public string ThumbnailPath { get; set; }

        public List<string> TypeNames { get; set; } = new List<string>();

        // Multi-tag user categories per item
        public List<string> UserCategories { get; set; } = new List<string>();
    }
}
