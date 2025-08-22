using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImAged.MVVM.ViewModel
{
    public ObservableCollection<FileItem> Files { get; set; }

    public FileViewModel()
    {
        // Mock data for now
        Files = new ObservableCollection<FileItem>
        {
            new FileItem { FileName = "image1.jpg", Modified = DateTime.Now, Status = "Active" },
            new FileItem { FileName = "doc1.pdf", Modified = DateTime.Now.AddDays(-1), Status = "Archived" }
        };
    }
}
