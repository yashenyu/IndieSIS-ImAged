using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImAged.MVVM.Model
{
    public class FileItem
    {
        public string FileName { get; set; }
        public string FileType { get; set; }
        public double FileSize { get; set; } // KB
        public string FilePath { get; set; }
        public DateTime Created { get; set; }
        public string State { get; set; } //  (e.g., "Active", "Expired")

        public string ImagePath { get; set; }

        public bool IsFolder { get; set; }
    }

}
