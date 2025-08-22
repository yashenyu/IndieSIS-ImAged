using ImAged.MVVM.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace ImAged.MVVM.ViewModel
{
    public class FileViewModel : INotifyPropertyChanged
    {
        private FileItem _selectedFile;
        public FileItem SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (_selectedFile != value)
                {
                    _selectedFile = value;
                    OnPropertyChanged(nameof(SelectedFile));
                }
            }
        }
        public ObservableCollection<FileItem> Files { get; set; }

        public FileViewModel()
        {
            Files = new ObservableCollection<FileItem>();
            LoadFiles();
        }
        private void LoadFiles()
        {
            string folderPath = @"C:\Users\Dre\Desktop";

            if (Directory.Exists(folderPath))
            {
                foreach (var path in Directory.GetFiles(folderPath, "*.ttl"))
                {
                    var info = new FileInfo(path);
                    Files.Add(new FileItem
                    {
                        FileName = info.Name,
                        FileType = info.Extension,
                        FileSize = info.Length / 1024d, // in KB
                        FilePath = info.FullName,
                        Created = info.CreationTime,
                        State = "Converted",
                        ImagePath = "Images/256x256.ico" // generic icon
                    });
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}