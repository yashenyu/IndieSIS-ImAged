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
            LoadFiles();
        }

        private void LoadFiles()
        {
            // Folders to search
            var targetFolders = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            foreach (var folderPath in targetFolders)
            {
                if (Directory.Exists(folderPath))
                {
                    foreach (var path in SafeEnumerateTtlFiles(folderPath))
                    {
                        var info = new FileInfo(path);
                        Files.Add(new FileItem
                        {
                            FileName = info.Name,
                            FileType = info.Extension,
                            FileSize = info.Length / 1024d, // in KB
                            FilePath = info.FullName,
                            Created = info.CreationTime,
                            State = GetFileState(path),
                            ImagePath = "256x256.ico" // generic icon
                        });
                    }
                }
            }
        }

        private IEnumerable<string> SafeEnumerateTtlFiles(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                IEnumerable<string> subdirs;
                try
                {
                    subdirs = Directory.EnumerateDirectories(current);
                }
                catch
                {
                    subdirs = Array.Empty<string>();
                }

                foreach (var dir in subdirs)
                {
                    pending.Push(dir);
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(current, "*.ttl", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    yield return file;
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

        private string GetFileState(string ttlPath)
        {
            var expiry = TryReadExpiryUtc(ttlPath);
            if (expiry == null) return "Unknown";

            var nowUtc = DateTimeOffset.UtcNow;
            var nearCutoff = nowUtc + TimeSpan.FromHours(24);

            if (expiry < nowUtc) return "Expired";
            else if (expiry <= nearCutoff) return "Near expiry";
            else return "Active";
        }

        // TTL layout: MAGIC(6)+salt(16)+nonce_hdr(12)+header(8 big-endian expiry)
        private DateTimeOffset? TryReadExpiryUtc(string ttlPath)
        {
            try
            {
                using (var fs = new FileStream(ttlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var buf = new byte[6 + 16 + 12 + 8];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int toRead = buf.Length - read;
                        int r = fs.Read(buf, read, toRead);
                        if (r <= 0) break;
                        read += r;
                    }
                    if (read < buf.Length) return null;
                    if (!(buf[0] == (byte)'I' && buf[1] == (byte)'M' && buf[2] == (byte)'A' && buf[3] == (byte)'G' && buf[4] == (byte)'E' && buf[5] == (byte)'D'))
                        return null;
                    int expOffset = 6 + 16 + 12;
                    ulong be = ((ulong)buf[expOffset + 0] << 56) | ((ulong)buf[expOffset + 1] << 48) | ((ulong)buf[expOffset + 2] << 40) | ((ulong)buf[expOffset + 3] << 32)
                             | ((ulong)buf[expOffset + 4] << 24) | ((ulong)buf[expOffset + 5] << 16) | ((ulong)buf[expOffset + 6] << 8) | buf[expOffset + 7];
                    if (be > (ulong)DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return null;
                    return DateTimeOffset.FromUnixTimeSeconds((long)be);
                }
            }
            catch
            {
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}