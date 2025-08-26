using ImAged.Core;
using ImAged.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Threading;

namespace ImAged.MVVM.ViewModel
{
    class MainViewModel : ObservableObject
    {
        public RelayCommand HomeViewCommand { get; set; } // HOME
        public RelayCommand ViewViewCommand { get; set; } // FAVORITES
        public RelayCommand FileViewCommand { get; set; } // ALL FILES
        public RelayCommand ConvertViewCommand { get; set; } // TOOLS / CONVERTER
        public RelayCommand SettingsViewCommand { get; set; } // SETTINGS

        public RelayCommand CloseCommand { get; set; }
        public RelayCommand MinimizeCommand { get; set; }

        public HomeViewModel HomeVm { get; set; }
        public ViewViewModel ViewVm { get; set; }
        public FileViewModel FileVm { get; set; }
        public ConvertViewModel ConvertVm { get; set; }
        public SettingsViewModel SettingVm { get; set; }

        private object _currentView;

        public object CurrentView
        {
            get { return _currentView; }
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        // Realtime file status counters
        private int _activeCount;
        public int ActiveCount
        {
            get => _activeCount;
            private set { _activeCount = value; OnPropertyChanged(); }
        }
        private int _nearExpiryCount;
        public int NearExpiryCount
        {
            get => _nearExpiryCount;
            private set { _nearExpiryCount = value; OnPropertyChanged(); }
        }
        private int _expiredCount;
        public int ExpiredCount
        {
            get => _expiredCount;
            private set { _expiredCount = value; OnPropertyChanged(); }
        }

        private readonly TimeSpan _nearExpiryWindow = TimeSpan.FromHours(24);
        private readonly DispatcherTimer _counterTimer = new DispatcherTimer();
        private readonly System.Collections.Generic.List<string> _searchDirectories = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<FileSystemWatcher> _counterWatchers = new System.Collections.Generic.List<FileSystemWatcher>();

        public MainViewModel()
        {
            var secureProcessManager = new SecureProcessManager();

            HomeVm = new HomeViewModel(secureProcessManager);
            ViewVm = new ViewViewModel();
            SettingVm = new SettingsViewModel();
            ConvertVm = new ConvertViewModel();
            FileVm = new FileViewModel();

            CurrentView = HomeVm;

            HomeViewCommand = new RelayCommand(o =>
            {
                CurrentView = HomeVm;
            });

            // Call the initialization method for ViewVm
            ViewViewCommand = new RelayCommand(async o =>
            {
                CurrentView = ViewVm;
                await ViewVm.InitializeFoldersAsync(); // <-- This line is the key
            });

            FileViewCommand = new RelayCommand(o =>
            {
                CurrentView = FileVm;
            });

            ConvertViewCommand = new RelayCommand(o =>
            {
                CurrentView = ConvertVm;
            });

            SettingsViewCommand = new RelayCommand(o =>
            {
                CurrentView = SettingVm;
            });

            CloseCommand = new RelayCommand(o =>
            {
                if (o is Window window)
                    window.Close();
            });

            MinimizeCommand = new RelayCommand(o =>
            {
                if (o is Window window)
                    window.WindowState = WindowState.Minimized;
            });

            InitializeSecureBackendAsync(secureProcessManager);

            // Directories used by gallery
            _searchDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            _searchDirectories.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            _searchDirectories.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

            // Timer updates as time passes
            _counterTimer.Interval = TimeSpan.FromSeconds(30);
            _counterTimer.Tick += async (_, __) => await UpdateCountersAsync();
            _counterTimer.Start();

            // Watchers for file changes
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
                    watcher.Created += async (_, __) => await UpdateCountersAsync();
                    watcher.Deleted += async (_, __) => await UpdateCountersAsync();
                    watcher.Renamed += async (_, __) => await UpdateCountersAsync();
                    _counterWatchers.Add(watcher);
                }
                catch { }
            }

            _ = UpdateCountersAsync();
        }

        private async void InitializeSecureBackendAsync(SecureProcessManager secureProcessManager)
        {
            try
            {
                await secureProcessManager.InitializeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize secure backend: {ex.Message}");
            }
        }

        private async Task UpdateCountersAsync()
        {
            try
            {
                var nowUtc = DateTimeOffset.UtcNow;
                var nearCutoff = nowUtc + _nearExpiryWindow;

                var files = new System.Collections.Generic.List<string>();
                foreach (var dir in _searchDirectories)
                {
                    if (Directory.Exists(dir))
                    {
                        try { files.AddRange(Directory.GetFiles(dir, "*.ttl", SearchOption.AllDirectories)); }
                        catch { }
                    }
                }

                int act = 0, near = 0, exp = 0;
                foreach (var f in files.Distinct())
                {
                    var expiry = TryReadExpiryUtc(f);
                    if (expiry == null) continue;
                    if (expiry < nowUtc) exp++;
                    else if (expiry <= nearCutoff) near++;
                    else act++;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ActiveCount = act;
                    NearExpiryCount = near;
                    ExpiredCount = exp;
                });
            }
            catch { }
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
    }
}