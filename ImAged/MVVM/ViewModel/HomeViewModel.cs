using ImAged.Core;
using ImAged.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImAged.MVVM.View;


namespace ImAged.MVVM.ViewModel
{
    public class TtlFileInfo : INotifyPropertyChanged
    {
        private string _filePath;
        private string _fileName;
        private BitmapSource _thumbnail;
        private bool _isLoading;
        private DateTime _lastModified;

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged(nameof(FilePath));
            }
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }

        public BitmapSource Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set
            {
                _lastModified = value;
                OnPropertyChanged(nameof(LastModified));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DateGroup : INotifyPropertyChanged
    {
        private DateTime _date;
        private int _imageCount;
        private ObservableCollection<TtlFileInfo> _files;

        public DateTime Date
        {
            get => _date;
            set
            {
                _date = value;
                OnPropertyChanged(nameof(Date));
                OnPropertyChanged(nameof(DisplayDate));
            }
        }

        public string DisplayDate
        {
            get
            {
                var day = _date.Day;
                var suffix = GetOrdinalSuffix(day);
                return $"{_date:MMMM d}{suffix}, {_date:yyyy} - {_imageCount} Images";
            }
        }

        private string GetOrdinalSuffix(int day)
        {
            if (day >= 11 && day <= 13)
                return "th";

            switch (day % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }

        public int ImageCount
        {
            get => _imageCount;
            set
            {
                _imageCount = value;
                OnPropertyChanged(nameof(ImageCount));
                OnPropertyChanged(nameof(DisplayDate));
            }
        }

        public ObservableCollection<TtlFileInfo> Files
        {
            get => _files;
            set
            {
                _files = value;
                OnPropertyChanged(nameof(Files));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    class HomeViewModel : ObservableObject, IDisposable
    {
        private readonly SecureProcessManager _secureProcessManager;
        private readonly List<string> _searchDirectories;
        private readonly List<FileSystemWatcher> _fileWatchers = new List<FileSystemWatcher>();

        // Memory management
        private readonly SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(2, 2); // Reduce to 2 concurrent
        private readonly Dictionary<string, SecureImageReference> _thumbnailCache = new Dictionary<string, SecureImageReference>();
        private readonly List<SecureImageReference> _activeImages = new List<SecureImageReference>();
        private readonly object _imagesLock = new object();
        private readonly DispatcherTimer _memoryCleanupTimer;
        private readonly int _maxCacheSize = 20;
        private readonly object _cacheLock = new object();

        private BitmapSource _displayedImage;
        private bool _isDisposed = false;
        private int _totalImagesLoaded = 0;
        public ObservableCollection<DateGroup> DateGroups { get; } = new ObservableCollection<DateGroup>();

        public BitmapSource DisplayedImage
        {
            get => _displayedImage;
            set
            {
                _displayedImage = value;
                OnPropertyChanged();
            }
        }

        public ICommand LoadTtlImageCommand { get; }
        public ICommand OpenTtlFileCommand { get; }

        public HomeViewModel(SecureProcessManager secureProcessManager)
        {
            _secureProcessManager = secureProcessManager;

            // Initialize search directories
            _searchDirectories = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            LoadTtlImageCommand = new RelayCommand(async (param) => await LoadTtlImageAsync());
            OpenTtlFileCommand = new RelayCommand(async (param) => await OpenTtlFileAsync(param));

            // Setup memory cleanup timer
            _memoryCleanupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10) // Check every 10 seconds
            };
            _memoryCleanupTimer.Tick += OnMemoryCleanupTimer;
            _memoryCleanupTimer.Start();

            // Auto-scan on startup
            _ = InitializeGalleryAsync();
            SetupFileWatching();
        }

        private void OnMemoryCleanupTimer(object sender, EventArgs e)
        {
            if (_isDisposed) return;

            // More aggressive cleanup for security
            ForceSecureMemoryCleanup();

            var process = System.Diagnostics.Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / (1024 * 1024);

            if (memoryMB > 500) // Lower threshold for security
            {
                System.Diagnostics.Debug.WriteLine($"High memory usage detected: {memoryMB}MB, forcing secure cleanup");
                ForceSecureMemoryCleanup();
            }
        }

        private void ForceSecureMemoryCleanup()
        {
            System.Diagnostics.Debug.WriteLine("Forcing secure memory cleanup");

            // Securely dispose all cached images
            lock (_cacheLock)
            {
                foreach (var secureRef in _thumbnailCache.Values)
                {
                    secureRef?.Dispose();
                }
                _thumbnailCache.Clear();
            }

            lock (_imagesLock)
            {
                foreach (var secureRef in _activeImages)
                {
                    secureRef?.Dispose();
                }
                _activeImages.Clear();
            }

            // Force immediate cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            System.Diagnostics.Debug.WriteLine("Secure memory cleanup completed");
        }

        private async Task InitializeGalleryAsync()
        {
            try
            {
                // Small delay to ensure UI is fully loaded
                await Task.Delay(100);
                await ScanForTtlFilesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gallery initialization error: {ex.Message}");
            }
        }

        private async Task ScanForTtlFilesAsync()
        {
            try
            {
                var allTtlFiles = new List<string>();

                // Scan all configured directories
                foreach (var directory in _searchDirectories)
                {
                    if (Directory.Exists(directory))
                    {
                        var ttlFiles = Directory.GetFiles(directory, "*.ttl", SearchOption.AllDirectories);
                        allTtlFiles.AddRange(ttlFiles);
                    }
                }

                // Remove duplicates and sort by last modified
                var uniqueFiles = allTtlFiles
                    .Distinct()
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                // Group files by date
                var groupedFiles = uniqueFiles
                    .GroupBy(f => File.GetLastWriteTime(f).Date)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                // Clear and recreate date groups on main thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DateGroups.Clear();
                });

                // Create date groups and add files
                foreach (var group in groupedFiles)
                {
                    var dateGroup = new DateGroup
                    {
                        Date = group.Key,
                        ImageCount = group.Count(),
                        Files = new ObservableCollection<TtlFileInfo>()
                    };

                    // Add files to the date group
                    foreach (var filePath in group)
                    {
                        var fileInfo = new TtlFileInfo
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileNameWithoutExtension(filePath),
                            LastModified = File.GetLastWriteTime(filePath)
                        };

                        dateGroup.Files.Add(fileInfo);

                        // Load thumbnail asynchronously
                        _ = Task.Run(async () => await LoadThumbnailAsync(fileInfo));
                    }

                    // Add date group to main collection on main thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DateGroups.Add(dateGroup);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning for TTL files: {ex.Message}");
            }
        }

        private async Task LoadThumbnailAsync(TtlFileInfo fileInfo)
        {
            // Check cache first
            lock (_cacheLock)
            {
                if (_thumbnailCache.TryGetValue(fileInfo.FilePath, out var secureRef))
                {
                    var cachedThumbnail = secureRef.GetImage();
                    if (cachedThumbnail != null)
                    {
                        fileInfo.Thumbnail = cachedThumbnail;
                        fileInfo.IsLoading = false;
                        return;
                    }
                    else
                    {
                        // Remove disposed reference
                        _thumbnailCache.Remove(fileInfo.FilePath);
                    }
                }
            }

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    fileInfo.IsLoading = true;
                });

                await _thumbnailSemaphore.WaitAsync();

                try
                {
                    if (_isDisposed) return;

                    // Load thumbnail with shorter timeout for security
                    var thumbnail = await _secureProcessManager.OpenTtlThumbnailAsync(fileInfo.FilePath, 256);

                    if (thumbnail != null)
                    {
                        // Create secure reference with short timeout
                        var secureRef = new SecureImageReference(thumbnail, null, 15); // 15 second timeout

                        lock (_cacheLock)
                        {
                            if (_thumbnailCache.Count >= _maxCacheSize)
                            {
                                // Dispose oldest reference securely
                                var oldestKey = _thumbnailCache.Keys.First();
                                _thumbnailCache[oldestKey]?.Dispose();
                                _thumbnailCache.Remove(oldestKey);
                            }
                            _thumbnailCache[fileInfo.FilePath] = secureRef;
                        }

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (!_isDisposed)
                            {
                                fileInfo.Thumbnail = thumbnail;
                                fileInfo.IsLoading = false;
                            }
                        });
                    }
                }
                finally
                {
                    _thumbnailSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for {fileInfo.FileName}: {ex.Message}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!_isDisposed)
                    {
                        fileInfo.IsLoading = false;
                    }
                });
            }
        }

        private async Task OpenTtlFileAsync(object parameter)
        {
            if (parameter is TtlFileInfo fileInfo)
            {
                System.Diagnostics.Debug.WriteLine("OpenTtlFileAsync called for: " + fileInfo.FilePath);
                try
                {
                    var bitmapSource = await _secureProcessManager.OpenTtlFileAsync(
                       fileInfo.FilePath, thumbnailMode: false);

                    if (bitmapSource != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var win = new ImageViewWindow(bitmapSource, fileInfo.FilePath);   // pass path
                            win.Show();
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error opening {fileInfo.FileName}: {ex.Message}");
                }
            }
        }

        private async Task LoadTtlImageAsync()
        {
            try
            {
                string ttlPath = @"C:\Users\Mark\Desktop\Test\jeon-somi-anime-7680x4320-23149.ttl";

                var bitmapSource = await _secureProcessManager.OpenTtlFileAsync(ttlPath);
                DisplayedImage = bitmapSource;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
            }
        }

        private void SetupFileWatching()
        {
            foreach (var directory in _searchDirectories)
            {
                if (Directory.Exists(directory))
                {
                    var watcher = new FileSystemWatcher(directory)
                    {
                        Filter = "*.ttl",
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnTtlFileCreated;
                    watcher.Deleted += OnTtlFileDeleted;
                    watcher.Renamed += OnTtlFileRenamed;

                    _fileWatchers.Add(watcher);
                }
            }
        }

        private async void OnTtlFileCreated(object sender, FileSystemEventArgs e)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var fileDate = File.GetLastWriteTime(e.FullPath).Date;
                var dateGroup = DateGroups.FirstOrDefault(g => g.Date == fileDate);

                if (dateGroup == null)
                {
                    // Create new date group
                    dateGroup = new DateGroup
                    {
                        Date = fileDate,
                        ImageCount = 1,
                        Files = new ObservableCollection<TtlFileInfo>()
                    };

                    // Insert at the beginning since it's the newest
                    DateGroups.Insert(0, dateGroup);
                }
                else
                {
                    dateGroup.ImageCount++;
                }

                var fileInfo = new TtlFileInfo
                {
                    FilePath = e.FullPath,
                    FileName = Path.GetFileNameWithoutExtension(e.FullPath),
                    LastModified = File.GetLastWriteTime(e.FullPath)
                };

                dateGroup.Files.Insert(0, fileInfo); // Add to beginning of group
                _ = Task.Run(async () => await LoadThumbnailAsync(fileInfo));
            });
        }

        private async void OnTtlFileDeleted(object sender, FileSystemEventArgs e)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var dateGroup in DateGroups)
                {
                    var fileToRemove = dateGroup.Files.FirstOrDefault(f => f.FilePath == e.FullPath);
                    if (fileToRemove != null)
                    {
                        dateGroup.Files.Remove(fileToRemove);
                        dateGroup.ImageCount--;

                        // Remove empty date groups
                        if (dateGroup.Files.Count == 0)
                        {
                            DateGroups.Remove(dateGroup);
                        }
                        break;
                    }
                }
            });
        }

        private async void OnTtlFileRenamed(object sender, RenamedEventArgs e)
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                foreach (var dateGroup in DateGroups)
                {
                    var oldFile = dateGroup.Files.FirstOrDefault(f => f.FilePath == e.OldFullPath);
                    if (oldFile != null)
                    {
                        var newDate = File.GetLastWriteTime(e.FullPath).Date;

                        if (newDate == dateGroup.Date)
                        {
                            // Same date, just update the file
                            oldFile.FilePath = e.FullPath;
                            oldFile.FileName = Path.GetFileNameWithoutExtension(e.FullPath);
                            oldFile.LastModified = File.GetLastWriteTime(e.FullPath);
                        }
                        else
                        {
                            // Different date, move to appropriate group
                            dateGroup.Files.Remove(oldFile);
                            dateGroup.ImageCount--;

                            var targetGroup = DateGroups.FirstOrDefault(g => g.Date == newDate);
                            if (targetGroup == null)
                            {
                                targetGroup = new DateGroup
                                {
                                    Date = newDate,
                                    ImageCount = 1,
                                    Files = new ObservableCollection<TtlFileInfo>()
                                };
                                DateGroups.Add(targetGroup);
                            }
                            else
                            {
                                targetGroup.ImageCount++;
                            }

                            oldFile.FilePath = e.FullPath;
                            oldFile.FileName = Path.GetFileNameWithoutExtension(e.FullPath);
                            oldFile.LastModified = File.GetLastWriteTime(e.FullPath);
                            targetGroup.Files.Add(oldFile);
                        }

                        // Remove empty date groups
                        if (dateGroup.Files.Count == 0)
                        {
                            DateGroups.Remove(dateGroup);
                        }
                        break;
                    }
                }
            });
        }

        public void Dispose()
        {
            _isDisposed = true;

            _memoryCleanupTimer?.Stop();

            // Force secure cleanup
            ForceSecureMemoryCleanup();

            foreach (var watcher in _fileWatchers)
            {
                watcher?.Dispose();
            }
            _fileWatchers.Clear();

            _thumbnailSemaphore?.Dispose();
        }

        private class SecureImageReference : IDisposable
        {
            private BitmapSource _image;
            private byte[] _encryptedData; // Keep encrypted version
            private readonly Timer _cleanupTimer;
            private bool _disposed = false;

            public SecureImageReference(BitmapSource image, byte[] encryptedData, int timeoutSeconds = 30)
            {
                _image = image;
                _encryptedData = encryptedData;

                // Setup immediate cleanup timer
                _cleanupTimer = new Timer(_ => Dispose(), null, TimeSpan.FromSeconds(timeoutSeconds), Timeout.InfiniteTimeSpan);
            }

            public BitmapSource GetImage()
            {
                if (_disposed) return null;
                return _image;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                // Securely clear the image from memory
                if (_image != null)
                {
                    try
                    {
                        // Overwrite memory with zeros before disposal
                        if (_image is WriteableBitmap wb)
                        {
                            wb.Lock();
                            var pixels = new byte[wb.PixelWidth * wb.PixelHeight * 4];
                            wb.WritePixels(new Int32Rect(0, 0, wb.PixelWidth, wb.PixelHeight), pixels, wb.PixelWidth * 4, 0);
                            wb.Unlock();
                        }
                    }
                    catch { /* Ignore errors during cleanup */ }

                    _image = null;
                }

                // Securely clear encrypted data
                if (_encryptedData != null)
                {
                    for (int i = 0; i < _encryptedData.Length; i++)
                    {
                        _encryptedData[i] = 0;
                    }
                    _encryptedData = null;
                }

                _cleanupTimer?.Dispose();

                // Force immediate cleanup
                GC.Collect();
            }
        }
    }
}