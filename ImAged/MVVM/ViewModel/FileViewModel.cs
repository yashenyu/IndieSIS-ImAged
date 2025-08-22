using ImAged.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImAged.MVVM.ViewModel
{
    public class FileViewModel
    {
        // This collection will be bound to your DataGrid/ListView
        public ObservableCollection<FileItem> Files { get; set; }

        public FileViewModel()
        {
            // Temporary test data (replace later with real logic)
            Files = new ObservableCollection<FileItem>
            {
                new FileItem { FileName = "image1.png", Created = DateTime.Now.AddDays(-1), Status = "Encrypted" },
                new FileItem { FileName = "image2.jpg", Created = DateTime.Now.AddHours(-5), Status = "Decrypted" },
                new FileItem { FileName = "document.pdf", Created = DateTime.Now.AddMinutes(-30), Status = "Pending" }
            };
        }
    }
}