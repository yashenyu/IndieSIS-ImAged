using ImAged.MVVM.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ImAged.MVVM.ViewModel
{
    public class FileViewModel : INotifyPropertyChanged
    {
        private FileItem _selectedFile;
        private string _searchText;
        private readonly List<string> _searchDirectories = new List<string>();
        private readonly List<FileSystemWatcher> _fileWatchers = new List<FileSystemWatcher>();

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

            InitializeSearchDirectories();
            LoadFiles();
            SetupFileWatching();
        }

        private void InitializeSearchDirectories()
        {
            _searchDirectories.Clear();
            _searchDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            _searchDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            _searchDirectories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        }

        private void LoadFiles()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Files.Clear();
            });

            foreach (var folderPath in _searchDirectories)
            {
                if (!Directory.Exists(folderPath)) continue;
                string[] paths;
                try
                {
                    paths = Directory.GetFiles(folderPath, "*.ttl", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (var path in paths)
                {
                    FileInfo info;
                    try { info = new FileInfo(path); }
                    catch { continue; }

                    Files.Add(new FileItem
                    {
                        FileName = info.Name,
                        FileType = info.Extension,
                        FileSize = info.Length / 1024d,
                        FilePath = info.FullName,
                        Created = info.CreationTime,
                        State = "Converted",
                        ImagePath = "256x256.ico"
                    });
                }
            }
        }

        private void SetupFileWatching()
        {
            foreach (var watcher in _fileWatchers)
            {
                try { watcher.Dispose(); } catch { }
            }
            _fileWatchers.Clear();

            foreach (var dir in _searchDirectories)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var watcher = new FileSystemWatcher(dir)
                    {
                        Filter = "*.ttl",
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnFileCreated;
                    watcher.Deleted += OnFileDeleted;
                    watcher.Renamed += OnFileRenamed;

                    _fileWatchers.Add(watcher);
                }
                catch { }
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                FileInfo info;
                try { info = new FileInfo(e.FullPath); }
                catch { return; }

                Files.Insert(0, new FileItem
                {
                    FileName = info.Name,
                    FileType = info.Extension,
                    FileSize = info.Exists ? info.Length / 1024d : 0,
                    FilePath = info.FullName,
                    Created = info.Exists ? info.CreationTime : DateTime.Now,
                    State = "Converted",
                    ImagePath = "256x256.ico"
                });
            });
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var toRemove = Files.FirstOrDefault(f => string.Equals(f.FilePath, e.FullPath, StringComparison.OrdinalIgnoreCase));
                if (toRemove != null)
                {
                    Files.Remove(toRemove);
                }
            });
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var item = Files.FirstOrDefault(f => string.Equals(f.FilePath, e.OldFullPath, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.FilePath = e.FullPath;
                    item.FileName = Path.GetFileName(e.FullPath);
                    try
                    {
                        var info = new FileInfo(e.FullPath);
                        item.FileType = info.Extension;
                        item.FileSize = info.Exists ? info.Length / 1024d : item.FileSize;
                        item.Created = info.Exists ? info.CreationTime : item.Created;
                    }
                    catch { }
                    OnPropertyChanged(nameof(Files));
                }
                else
                {
                    // If it wasn't in list (e.g., moved from outside into watched folder), treat as create
                    OnFileCreated(sender, new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(e.FullPath) ?? string.Empty, Path.GetFileName(e.FullPath)));
                }
            });
        }

        private bool FilterFiles(object obj)
        {
            if (obj is FileItem file)
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                    return true;

                return file.FileName.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                       file.FileType.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
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