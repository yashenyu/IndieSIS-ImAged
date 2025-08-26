using ImAged.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;

namespace ImAged.MVVM.ViewModel
{
    public class FileViewModel : INotifyPropertyChanged
    {
        private FileItem _selectedFile;
        private string _searchText;

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
        public ICollectionView FilesView { get; set; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));
                    FilesView.Refresh(); // apply filter when search text changes
                }
            }
        }

        public FileViewModel()
        {
            Files = new ObservableCollection<FileItem>();
            FilesView = CollectionViewSource.GetDefaultView(Files);
            FilesView.Filter = FilterFiles;

            LoadFiles();
        }

        private void LoadFiles()
        {
            // Folders to search
            var targetFolders = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            foreach (var folderPath in targetFolders)
            {
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
                            ImagePath = "256x256.ico" // generic icon
                        });
                    }
                }
            }
        }
        private bool FilterFiles(object obj)
        {
            if (obj is FileItem file)
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                    return true;

                return file.FileName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       file.FileType.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}