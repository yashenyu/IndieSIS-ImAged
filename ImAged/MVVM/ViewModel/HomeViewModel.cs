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
using ImAged;


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

        private DateTimeOffset? _expirationUtc;
        public DateTimeOffset? ExpirationUtc
        {
            get => _expirationUtc;
            set { _expirationUtc = value; OnPropertyChanged(nameof(ExpirationUtc)); OnPropertyChanged(nameof(EstimatedRemainingTime)); }
        }

        public DateTime? ExpirationLocal
        {
            get => ExpirationUtc?.LocalDateTime;
        }

        public string EstimatedRemainingTime
        {
            get
            {
                if (ExpirationUtc == null) return "";
                var remaining = ExpirationUtc.Value - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) return "Expired";
                
                var days = (int)remaining.TotalDays;
                var hours = remaining.Hours;
                var minutes = remaining.Minutes;
                
                if (days > 0)
                    return $"{days}d";           // Only show days when > 1 day
                else if (hours > 0)
                    return $"{hours}h {minutes}m"; // Show hours and minutes when < 1 day
                else
                    return $"{minutes}m";          // Show only minutes when < 1 hour
            }
        }

        public ICommand OpenInFolderCommand { get; set; }
        public ICommand DeleteImageCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void RefreshCountdown()
        {
            OnPropertyChanged(nameof(EstimatedRemainingTime));
        }

        public bool IsExpired => ExpirationUtc.HasValue && ExpirationUtc.Value <= DateTimeOffset.UtcNow;

        private bool _expiredHandled;
        public bool ExpiredHandled
        {
            get => _expiredHandled;
            set
            {
                _expiredHandled = value;
                OnPropertyChanged(nameof(ExpiredHandled));
            }
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
        private readonly SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(3, 3); // Reduced from 10 to 3 concurrent
        private readonly Dictionary<string, SecureImageReference> _thumbnailCache = new Dictionary<string, SecureImageReference>();
        private readonly List<SecureImageReference> _activeImages = new List<SecureImageReference>();
        private readonly object _imagesLock = new object();
        private readonly DispatcherTimer _memoryCleanupTimer;
        private readonly int _maxCacheSize = 20; // Lowered from 50 to 20 for better memory usage
        private readonly object _cacheLock = new object();

        // NEW: master list & search text for filtering
        private readonly List<DateGroup> _allDateGroups = new List<DateGroup>();

        // Real-time expiration tracking
        private readonly Dictionary<string, ImageViewWindow> _openWindows = new Dictionary<string, ImageViewWindow>();
        private readonly object _windowsLock = new object();
        private readonly DispatcherTimer _expirationTimer;
        private readonly TimeSpan _expirationCheckInterval = TimeSpan.FromSeconds(1); // Check every second

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                ApplyFilter();
            }
        }

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

        private readonly DispatcherTimer _countdownTimer = new DispatcherTimer();

        public HomeViewModel() : this(App.SecureProcessManagerInstance) { }

        public HomeViewModel(SecureProcessManager secureProcessManager)
        {
            _secureProcessManager = secureProcessManager;

            // Initialize search directories
            _searchDirectories = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            OpenTtlFileCommand = new RelayCommand(
                async (param) => await OpenTtlFileAsync(param),
                (param) => param is TtlFileInfo);

            // Setup memory cleanup timer (disabled at runtime)
            _memoryCleanupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            // Disabled: do not hook Tick or Start to avoid lag spikes

            // Simple minute-based updates (much less laggy)
            _countdownTimer.Interval = TimeSpan.FromMinutes(1);
            _countdownTimer.Tick += (_, __) =>
            {
                foreach (var g in DateGroups)
                    foreach (var f in g.Files)
                        f?.RefreshCountdown();
            };
            _countdownTimer.Start();

            // Real-time expiration timer
            _expirationTimer = new DispatcherTimer
            {
                Interval = _expirationCheckInterval
            };
            _expirationTimer.Tick += OnExpirationTimerTick;
            _expirationTimer.Start();

            // Subscribe to window lifecycle events
            ImageViewWindow.WindowOpened += OnImageViewWindowOpened;
            ImageViewWindow.WindowClosed += OnImageViewWindowClosed;

            // Auto-scan on startup
            _ = InitializeGalleryAsync();
            SetupFileWatching();
        }

        private void OnMemoryCleanupTimer(object sender, EventArgs e)
        {
            // Disabled: avoid runtime cleanup to prevent lag spikes; cleanup happens on Dispose()
            if (_isDisposed) return;
        }

        private void OnExpirationTimerTick(object sender, EventArgs e)
        {
            if (_isDisposed) return;

            CheckMemoryUsageAndCleanup();

            try
            {
                var expiredFiles = new List<TtlFileInfo>();

                foreach (var dateGroup in DateGroups)
                {
                    foreach (var fileInfo in dateGroup.Files.ToList())
                    {
                        fileInfo.RefreshCountdown();

                        // Only handle if not already handled
                        if (fileInfo.IsExpired && !fileInfo.ExpiredHandled)
                        {
                            expiredFiles.Add(fileInfo);
                        }
                    }
                }

                foreach (var expiredFile in expiredFiles)
                {
                    HandleExpiredFile(expiredFile);
                    expiredFile.ExpiredHandled = true; // Mark as handled
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in expiration timer: {ex.Message}");
            }
        }

        private void CheckMemoryUsageAndCleanup()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var memoryUsageMB = currentProcess.WorkingSet64 / 1024 / 1024;
                
                // If memory usage is over 500MB, force cleanup
                if (memoryUsageMB > 500)
                {
                    System.Diagnostics.Debug.WriteLine($"High memory usage detected: {memoryUsageMB} MB. Forcing cleanup...");
                    ForceSecureMemoryCleanup();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking memory usage: {ex.Message}");
            }
        }

        private void HandleExpiredFile(TtlFileInfo expiredFile)
        {
            try
            {
                // Close any open window for this file
                CloseImageWindow(expiredFile.FilePath);

                // Set thumbnail to black and clear memory
                ClearFileMemory(expiredFile);

                // Note: We don't remove from UI anymore - just make thumbnail black
                // RemoveFileFromUI(expiredFile);

                System.Diagnostics.Debug.WriteLine($"Handled expired file: {expiredFile.FileName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling expired file {expiredFile.FileName}: {ex.Message}");
            }
        }

        private void CloseImageWindow(string filePath)
        {
            lock (_windowsLock)
            {
                if (_openWindows.TryGetValue(filePath, out var window))
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (window != null && window.IsVisible)
                            {
                                window.Close();
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error closing window for {filePath}: {ex.Message}");
                    }
                    finally
                    {
                        _openWindows.Remove(filePath);
                    }
                }
            }
        }

        private void ClearFileMemory(TtlFileInfo fileInfo)
        {
            // Clear thumbnail from cache
            lock (_cacheLock)
            {
                if (_thumbnailCache.TryGetValue(fileInfo.FilePath, out var secureRef))
                {
                    secureRef?.Dispose();
                    _thumbnailCache.Remove(fileInfo.FilePath);
                }
            }

            // Set thumbnail to black instead of clearing it
            Application.Current.Dispatcher.Invoke(() =>
            {
                fileInfo.Thumbnail = CreateBlackThumbnail();
            });
        }

        private BitmapSource CreateBlackThumbnail()
        {
            // Create a 256x256 black bitmap
            const int size = 256;
            var pixels = new byte[size * size * 4]; // 4 bytes per pixel (BGRA)
            
            // Fill with black pixels (all zeros)
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0;     // Blue
                pixels[i + 1] = 0; // Green
                pixels[i + 2] = 0; // Red
                pixels[i + 3] = 255; // Alpha (fully opaque)
            }

            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            bitmap.Freeze(); // Make it thread-safe

            return bitmap;
        }

        private void RemoveFileFromUI(TtlFileInfo fileInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Remove from current view
                foreach (var dateGroup in DateGroups.ToList())
                {
                    if (dateGroup.Files.Contains(fileInfo))
                    {
                        dateGroup.Files.Remove(fileInfo);
                        dateGroup.ImageCount--;

                        if (dateGroup.Files.Count == 0)
                        {
                            DateGroups.Remove(dateGroup);
                        }
                        break;
                    }
                }

                // Remove from master list
                foreach (var dateGroup in _allDateGroups.ToList())
                {
                    if (dateGroup.Files.Contains(fileInfo))
                    {
                        dateGroup.Files.Remove(fileInfo);
                        dateGroup.ImageCount--;

                        if (dateGroup.Files.Count == 0)
                        {
                            _allDateGroups.Remove(dateGroup);
                        }
                        break;
                    }
                }
            });
        }



        private void OnImageViewWindowOpened(object sender, ImageViewWindowEventArgs e)
        {
            lock (_windowsLock)
            {
                _openWindows[e.FilePath] = e.Window;
            }
        }

        private void OnImageViewWindowClosed(object sender, ImageViewWindowEventArgs e)
        {
            lock (_windowsLock)
            {
                if (_openWindows.ContainsKey(e.FilePath))
                {
                    var window = _openWindows[e.FilePath];
                    _openWindows.Remove(e.FilePath);
                    
                    // Ensure the window is properly disposed
                    try
                    {
                        if (window != null && !window.IsDisposed)
                        {
                            window.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error disposing window: {ex.Message}");
                    }
                }
            }
            
            // Force aggressive garbage collection to free up memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // Also trigger secure memory cleanup
            try
            {
                _secureProcessManager?.ForceMemoryCleanup();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during secure cleanup after window close: {ex.Message}");
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

            // Force cleanup in the secure process manager
            try
            {
                _secureProcessManager?.ForceMemoryCleanup();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during secure process manager cleanup: {ex.Message}");
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            System.Diagnostics.Debug.WriteLine("Secure memory cleanup completed");
        }

        private async Task InitializeGalleryAsync()
        {
            try
            {
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

     
                foreach (var directory in _searchDirectories)
                {
                    if (Directory.Exists(directory))
                    {
                        var ttlFiles = Directory.GetFiles(directory, "*.ttl", SearchOption.AllDirectories);
                        allTtlFiles.AddRange(ttlFiles);
                    }
                }

                var uniqueFiles = allTtlFiles
                    .Distinct()
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                var groupedFiles = uniqueFiles
                    .GroupBy(f => File.GetLastWriteTime(f).Date)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DateGroups.Clear();
                });

                foreach (var group in groupedFiles)
                {
                    var dateGroup = new DateGroup
                    {
                        Date = group.Key,
                        ImageCount = group.Count(),
                        Files = new ObservableCollection<TtlFileInfo>()
                    };

                    foreach (var filePath in group)
                    {
                        var fileInfo = new TtlFileInfo
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileNameWithoutExtension(filePath),
                            LastModified = File.GetLastWriteTime(filePath),
                            ExpiredHandled = false // Ensure reset
                        };

                        fileInfo.ExpirationUtc = TryReadExpiryUtc(fileInfo.FilePath);

                        fileInfo.OpenInFolderCommand = new RelayCommand(_ => OpenInFolder(fileInfo));
                        fileInfo.DeleteImageCommand = new RelayCommand(async _ => await DeleteImageAsync(fileInfo));

                        dateGroup.Files.Add(fileInfo);

                        // Only load thumbnail if file is not expired (expired files get black thumbnail immediately)
                        if (!fileInfo.IsExpired)
                        {
                            _ = Task.Run(async () => await LoadThumbnailAsync(fileInfo));
                        }
                        else
                        {
                            // Set black thumbnail immediately for expired files
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                fileInfo.Thumbnail = CreateBlackThumbnail();
                            });
                        }
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DateGroups.Add(dateGroup);
                    });
                }

                _allDateGroups.Clear();
                _allDateGroups.AddRange(DateGroups);

                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning for TTL files: {ex.Message}");
            }
        }

        private async Task LoadThumbnailAsync(TtlFileInfo fileInfo)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 500;

            lock (_cacheLock)
            {
                if (_thumbnailCache.TryGetValue(fileInfo.FilePath, out var secureRef))
                {
                    var cachedThumbnail = secureRef.GetImage();
                    if (cachedThumbnail != null && IsValidBitmapSource(cachedThumbnail))
                    {
                        fileInfo.Thumbnail = cachedThumbnail;
                        fileInfo.IsLoading = false;
                        return;
                    }
                    else
                    {
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

                    BitmapSource thumbnail = null;
                    Exception lastException = null;

                    // Retry logic for thumbnail loading
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"Loading thumbnail for {fileInfo.FileName} (attempt {attempt}/{maxRetries})");
                            
                            thumbnail = await _secureProcessManager.OpenTtlThumbnailAsync(fileInfo.FilePath, 256);

                            // Validate the thumbnail
                            if (thumbnail != null && IsValidBitmapSource(thumbnail))
                            {
                                System.Diagnostics.Debug.WriteLine($"Successfully loaded thumbnail for {fileInfo.FileName} on attempt {attempt}");
                                break;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Invalid thumbnail received for {fileInfo.FileName} on attempt {attempt}");
                                thumbnail = null;
                                
                                if (attempt < maxRetries)
                                {
                                    await Task.Delay(retryDelayMs);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for {fileInfo.FileName} on attempt {attempt}: {ex.Message}");
                            
                            if (attempt < maxRetries)
                            {
                                await Task.Delay(retryDelayMs);
                            }
                        }
                    }

                    if (thumbnail != null && IsValidBitmapSource(thumbnail))
                    {
                        var secureRef = new SecureImageReference(thumbnail, null, 15);

                        lock (_cacheLock)
                        {
                            if (_thumbnailCache.Count >= _maxCacheSize)
                            {
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
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load valid thumbnail for {fileInfo.FileName} after {maxRetries} attempts");
                        if (lastException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Last exception: {lastException.Message}");
                        }

                        // Create a placeholder thumbnail instead of leaving it black
                        var placeholder = CreatePlaceholderThumbnail(256);
                        if (placeholder != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (!_isDisposed)
                                {
                                    fileInfo.Thumbnail = placeholder;
                                    fileInfo.IsLoading = false;
                                }
                            });
                        }
                        else
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (!_isDisposed)
                                {
                                    fileInfo.IsLoading = false;
                                }
                            });
                        }
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

        private bool IsValidBitmapSource(BitmapSource bitmapSource)
        {
            try
            {
                if (bitmapSource == null)
                    return false;

                // Check if the bitmap has valid dimensions
                if (bitmapSource.PixelWidth <= 0 || bitmapSource.PixelHeight <= 0)
                    return false;

                // Check if the bitmap is frozen (required for cross-thread access)
                if (!bitmapSource.IsFrozen)
                    return false;

                // Additional validation: check if the bitmap has actual pixel data
                // This is a basic check - you could add more sophisticated validation if needed
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating BitmapSource: {ex.Message}");
                return false;
            }
        }

        private BitmapSource CreatePlaceholderThumbnail(int size = 256)
        {
            try
            {
                var writeableBitmap = new WriteableBitmap(
                    size, size, 96, 96, PixelFormats.Bgra32, null);

                writeableBitmap.Lock();

                // Create a simple placeholder with a gray background and text
                var pixels = new byte[size * size * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    // Gray background
                    pixels[i] = 128;     // Blue
                    pixels[i + 1] = 128; // Green
                    pixels[i + 2] = 128; // Red
                    pixels[i + 3] = 255; // Alpha
                }

                writeableBitmap.WritePixels(
                    new Int32Rect(0, 0, size, size),
                    pixels,
                    size * 4,
                    0);

                writeableBitmap.Unlock();
                writeableBitmap.Freeze();

                return writeableBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating placeholder thumbnail: {ex.Message}");
                return null;
            }
        }

        private async Task OpenTtlFileAsync(object parameter)
        {
            if (parameter is TtlFileInfo fileInfo)
            {
                System.Diagnostics.Debug.WriteLine("OpenTtlFileAsync called for: " + fileInfo.FilePath);
                
                // Set loading cursor
                var originalCursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                // Auto-reset cursor after 5 seconds to avoid stuck wait cursor
                var cursorResetCts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cursorResetCts.Token);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (Mouse.OverrideCursor == Cursors.Wait)
                            {
                                Mouse.OverrideCursor = originalCursor;
                            }
                        });
                    }
                    catch (TaskCanceledException) { }
                });
                
                try
                {
                    var bitmapSource = await _secureProcessManager.OpenTtlFileAsync(
                       fileInfo.FilePath, thumbnailMode: false);

                    if (bitmapSource != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var win = new ImageViewWindow(bitmapSource, fileInfo.FilePath);
                            win.Show();
                        });
                    }
                    else
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show("Unable to open this TTL image. The secure backend returned no data.", "Open Image", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error opening {fileInfo.FileName}: {ex.Message}");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Error opening image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    // Restore original cursor
                    try { cursorResetCts.Cancel(); } catch { }
                    Mouse.OverrideCursor = originalCursor;
                }
            }
        }

        private void OpenInFolder(TtlFileInfo fileInfo)
        {
            if (fileInfo == null) return;
            try
            {
                if (File.Exists(fileInfo.FilePath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fileInfo.FilePath}\"");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening folder for {fileInfo.FileName}: {ex.Message}");
            }
        }

        private async Task DeleteImageAsync(TtlFileInfo fileInfo)
        {
            if (fileInfo == null) return;

            try
            {
                var result = MessageBox.Show(
                    $"Delete '{fileInfo.FileName}'? This cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                if (File.Exists(fileInfo.FilePath))
                {
                    File.Delete(fileInfo.FilePath);
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var groupContaining = DateGroups.FirstOrDefault(g => g.Files.Contains(fileInfo));
                    if (groupContaining != null)
                    {
                        groupContaining.Files.Remove(fileInfo);
                        groupContaining.ImageCount--;
                        if (groupContaining.Files.Count == 0)
                        {
                            DateGroups.Remove(groupContaining);
                        }
                    }

                    var masterGroup = _allDateGroups.FirstOrDefault(g => g.Date == fileInfo.LastModified.Date);
                    if (masterGroup != null)
                    {
                        var toRemove = masterGroup.Files.FirstOrDefault(f => f.FilePath == fileInfo.FilePath);
                        if (toRemove != null)
                        {
                            masterGroup.Files.Remove(toRemove);
                            masterGroup.ImageCount = masterGroup.Files.Count;
                        }
                        if (masterGroup.Files.Count == 0)
                        {
                            _allDateGroups.Remove(masterGroup);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting {fileInfo.FileName}: {ex.Message}");
                MessageBox.Show($"Failed to delete file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    dateGroup = new DateGroup
                    {
                        Date = fileDate,
                        ImageCount = 1,
                        Files = new ObservableCollection<TtlFileInfo>()
                    };

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
                    LastModified = File.GetLastWriteTime(e.FullPath),
                    ExpiredHandled = false // Ensure reset
                };

                fileInfo.ExpirationUtc = TryReadExpiryUtc(fileInfo.FilePath);

                fileInfo.OpenInFolderCommand = new RelayCommand(_ => OpenInFolder(fileInfo));
                fileInfo.DeleteImageCommand = new RelayCommand(async _ => await DeleteImageAsync(fileInfo));

                dateGroup.Files.Insert(0, fileInfo);
                
                // Check if file is already expired
                if (fileInfo.IsExpired)
                {
                    // Set thumbnail to black immediately for already expired files
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        fileInfo.Thumbnail = CreateBlackThumbnail();
                    });
                }
                else
                {
                    _ = Task.Run(async () => await LoadThumbnailAsync(fileInfo));
                }
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
                            oldFile.FilePath = e.FullPath;
                            oldFile.FileName = Path.GetFileNameWithoutExtension(e.FullPath);
                            oldFile.LastModified = File.GetLastWriteTime(e.FullPath);
                            oldFile.ExpirationUtc = TryReadExpiryUtc(oldFile.FilePath);
                        }
                        else
                        {
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
                            oldFile.ExpirationUtc = TryReadExpiryUtc(oldFile.FilePath);
                            targetGroup.Files.Add(oldFile);
                        }

                        if (dateGroup.Files.Count == 0)
                        {
                            DateGroups.Remove(dateGroup);
                        }
                        break;
                    }
                }
            });
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                SyncUi(() =>
                {
                    DateGroups.Clear();
                    foreach (var g in _allDateGroups) DateGroups.Add(g);
                });
                return;
            }

            var term = _searchText.Trim();
            var filtered = _allDateGroups
                .Select(g =>
                {
                    var matches = g.Files.Where(f => f.FileName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (matches.Count == 0) return null;
                    return new DateGroup
                    {
                        Date = g.Date,
                        ImageCount = matches.Count,
                        Files = new ObservableCollection<TtlFileInfo>(matches)
                    };
                })
                .Where(g => g != null)
                .ToList();

            SyncUi(() =>
            {
                DateGroups.Clear();
                foreach (var g in filtered) DateGroups.Add(g);
            });
        }

        private static void SyncUi(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }

        public void Dispose()
        {
            _isDisposed = true;

            _memoryCleanupTimer?.Stop();
            _countdownTimer?.Stop();
            _expirationTimer?.Stop();

            // Unsubscribe from window events
            ImageViewWindow.WindowOpened -= OnImageViewWindowOpened;
            ImageViewWindow.WindowClosed -= OnImageViewWindowClosed;

            // Close all open windows
            lock (_windowsLock)
            {
                foreach (var window in _openWindows.Values)
                {
                    try
                    {
                        if (window != null && window.IsVisible)
                        {
                            Application.Current.Dispatcher.Invoke(() => window.Close());
                        }
                    }
                    catch { /* Ignore errors during cleanup */ }
                }
                _openWindows.Clear();
            }

            ForceSecureMemoryCleanup();

            foreach (var watcher in _fileWatchers)
            {
                watcher?.Dispose();
            }
            _fileWatchers.Clear();

            _thumbnailSemaphore?.Dispose();
        }

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
                        int r = fs.Read(buf, read, buf.Length - read);
                        if (r <= 0) break;
                        read += r;
                    }
                    if (read < buf.Length) return null;
                    if (!(buf[0]=='I'&&buf[1]=='M'&&buf[2]=='A'&&buf[3]=='G'&&buf[4]=='E'&&buf[5]=='D')) return null;
                    int expOffset = 6 + 16 + 12;
                    ulong be = ((ulong)buf[expOffset+0] << 56)|((ulong)buf[expOffset+1] << 48)|((ulong)buf[expOffset+2] << 40)|((ulong)buf[expOffset+3] << 32)
                              |((ulong)buf[expOffset+4] << 24)|((ulong)buf[expOffset+5] << 16)|((ulong)buf[expOffset+6] << 8)|buf[expOffset+7];
                    if (be > (ulong)DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return null;
                    return DateTimeOffset.FromUnixTimeSeconds((long)be);
                }
            }
            catch { return null; }
        }

        private class SecureImageReference : IDisposable
        {
            private BitmapSource _image;
            private byte[] _encryptedData;
            private readonly Timer _cleanupTimer;
            private bool _disposed = false;

            public SecureImageReference(BitmapSource image, byte[] encryptedData, int timeoutSeconds = 30)
            {
                _image = image;
                _encryptedData = encryptedData;

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

                if (_image != null)
                {
                    try
                    {
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

                if (_encryptedData != null)
                {
                    for (int i = 0; i < _encryptedData.Length; i++)
                    {
                        _encryptedData[i] = 0;
                    }
                    _encryptedData = null;
                }

                _cleanupTimer?.Dispose();

                GC.Collect();
            }
        }
    }
}