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
        public DateTime Modified { get; set; }
        public string Status { get; set; }
    }
}
