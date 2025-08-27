using ImAged.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows;
using ImAged.Services;

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
                    _ = LoadSelectedThumbnailAsync();
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

        private SecureProcessManager _secureProcessManager;
        private bool _secureInitialized;

        private async Task EnsureSecureAsync()
        {
            if (_secureProcessManager == null)
            {
                _secureProcessManager = new SecureProcessManager();
            }
            if (!_secureInitialized)
            {
                try
                {
                    await _secureProcessManager.InitializeAsync();
                    _secureInitialized = true;
                }
                catch
                {
                    _secureInitialized = false;
                }
            }
        }

        private async Task LoadSelectedThumbnailAsync()
        {
            var file = _selectedFile;
            if (file == null) return;
            try
            {
                // If expired, show default logo immediately
                if (string.Equals(file.State, "Expired", StringComparison.OrdinalIgnoreCase))
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => file.Thumbnail = GetDefaultLogo());
                    return;
                }

                await EnsureSecureAsync();
                if (!_secureInitialized)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => file.Thumbnail = GetDefaultLogo());
                    return;
                }

                var bmp = await _secureProcessManager.OpenTtlThumbnailAsync(file.FilePath, 256);
                await Application.Current.Dispatcher.InvokeAsync(() => file.Thumbnail = bmp ?? GetDefaultLogo());
            }
            catch
            {
                // On any error, set default logo
                try
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => file.Thumbnail = GetDefaultLogo());
                }
                catch { }
            }
        }

        private BitmapSource _defaultLogo;
        private BitmapSource GetDefaultLogo()
        {
            if (_defaultLogo != null) return _defaultLogo;
            try
            {
                var uri = new Uri("pack://application:,,,/ImAged;component/Images/base logo.png", UriKind.Absolute);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = uri;
                img.EndInit();
                img.Freeze();
                _defaultLogo = img;
                return _defaultLogo;
            }
            catch
            {
                return null;
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
                        var imagePath = TryFindAssociatedImagePath(path) ?? "256x256.ico";
                        Files.Add(new FileItem
                        {
                            FileName = info.Name,
                            FileType = info.Extension,
                            FileSize = info.Length / 1024d, // in KB
                            FilePath = info.FullName,
                            Created = info.CreationTime,
                            State = GetFileState(path),
                            ImagePath = imagePath
                        });
                    }
                }
            }
        }

        private string TryFindAssociatedImagePath(string ttlPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(ttlPath);
                var baseName = Path.GetFileNameWithoutExtension(ttlPath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
                    return null;

                string[] candidateExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
                foreach (var ext in candidateExtensions)
                {
                    var candidate = Path.Combine(directory, baseName + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }

                // Also probe for common suffix patterns (e.g., "-image", "_image")
                string[] suffixes = new[] { "-image", "_image", "-img", "_img" };
                foreach (var suffix in suffixes)
                {
                    foreach (var ext in candidateExtensions)
                    {
                        var candidate = Path.Combine(directory, baseName + suffix + ext);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }

                // Look into common subfolders one-level deep (e.g., images/, img/, thumbs/)
                string[] commonChildFolders = new[] { "images", "image", "img", "imgs", "thumb", "thumbs", "thumbnails" };
                foreach (var child in commonChildFolders)
                {
                    var childDir = Path.Combine(directory, child);
                    if (Directory.Exists(childDir))
                    {
                        foreach (var ext in candidateExtensions)
                        {
                            var candidate = Path.Combine(childDir, baseName + ext);
                            if (File.Exists(candidate))
                                return candidate;

                            foreach (var suffix in suffixes)
                            {
                                var candidateWithSuffix = Path.Combine(childDir, baseName + suffix + ext);
                                if (File.Exists(candidateWithSuffix))
                                    return candidateWithSuffix;
                            }
                        }
                    }
                }

                // Probe parent directory (one level up)
                var parentDir = Directory.GetParent(directory)?.FullName;
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    foreach (var ext in candidateExtensions)
                    {
                        var candidate = Path.Combine(parentDir, baseName + ext);
                        if (File.Exists(candidate))
                            return candidate;

                        foreach (var suffix in suffixes)
                        {
                            var candidateWithSuffix = Path.Combine(parentDir, baseName + suffix + ext);
                            if (File.Exists(candidateWithSuffix))
                                return candidateWithSuffix;
                        }
                    }

                    // Common image folders at parent level
                    foreach (var child in commonChildFolders)
                    {
                        var childDir = Path.Combine(parentDir, child);
                        if (Directory.Exists(childDir))
                        {
                            foreach (var ext in candidateExtensions)
                            {
                                var candidate = Path.Combine(childDir, baseName + ext);
                                if (File.Exists(candidate))
                                    return candidate;

                                foreach (var suffix in suffixes)
                                {
                                    var candidateWithSuffix = Path.Combine(childDir, baseName + suffix + ext);
                                    if (File.Exists(candidateWithSuffix))
                                        return candidateWithSuffix;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore and fall back
            }
            return null;
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