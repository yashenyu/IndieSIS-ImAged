using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using ImAged.Core;
using ImAged.Services;

namespace ImAged.MVVM.ViewModel
{
    public class ConvertViewModel : INotifyPropertyChanged
    {
        private readonly SecureProcessManager _secureManager = new SecureProcessManager();

        // ---------------- Fields ----------------
        private string _selectedFile;
        private string _statusMessage;
        private string _day = "";
        private string _month = "";
        private string _year = "";
        private string _hour = "";
        private string _minute = "";
        private bool _isPm;

        // ---------------- Properties ----------------
        public ObservableCollection<string> ConversionLogs { get; } = new ObservableCollection<string>();

        public string SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged(nameof(SelectedFile));
                OnPropertyChanged(nameof(SelectFileButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string DigitsOnly(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return new string(input.Where(char.IsDigit).Take(2).ToArray());
        }

        public string Day    { get => _day;    set { _day    = DigitsOnly(value); OnPropertyChanged(nameof(Day));    } }
        public string Month  { get => _month;  set { _month  = DigitsOnly(value); OnPropertyChanged(nameof(Month));  } }
        public string Year   { get => _year;   set { _year   = DigitsOnly(value); OnPropertyChanged(nameof(Year));   } }
        public string Hour   { get => _hour;   set { _hour   = DigitsOnly(value); OnPropertyChanged(nameof(Hour));   } }
        public string Minute { get => _minute; set { _minute = DigitsOnly(value); OnPropertyChanged(nameof(Minute)); } }

        public bool IsPM
        {
            get => _isPm;
            set { _isPm = value; OnPropertyChanged(nameof(IsPM)); OnPropertyChanged(nameof(AmPmLabel)); }
        }

        public string AmPmLabel => IsPM ? "PM" : "AM";

        public string SelectFileButtonText
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedFile))
                    return "+  Select File";

                var fileName = System.IO.Path.GetFileName(SelectedFile);

                // If filename is longer than 20 characters, truncate it
                if (fileName.Length > 20)
                {
                    var extension = System.IO.Path.GetExtension(fileName);
                    var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);

                    // Keep first 8 characters + "..." + extension
                    var truncatedName = nameWithoutExt.Substring(0, 8) + "..." + extension;
                    return truncatedName;
                }

                return fileName;
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        // ---------------- Commands ----------------
        public RelayCommand SelectFileCommand { get; }
        public RelayCommand ToggleAmPmCommand { get; }
        public RelayCommand ConvertCommand    { get; }

        // ---------------- Constructor ----------------
        public ConvertViewModel()
        {
            SelectFileCommand = new RelayCommand(_ => ExecuteSelectFile());
            ToggleAmPmCommand = new RelayCommand(_ => IsPM = !IsPM);
            ConvertCommand    = new RelayCommand(async _ => await ExecuteConvertAsync(), _ => !string.IsNullOrEmpty(SelectedFile));

            _ = _secureManager.InitializeAsync();
        }

        // ---------------- Methods ----------------
        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            
            // Add to the beginning to show newest first
            ConversionLogs.Insert(0, logEntry);
            
            // Keep only the last 20 logs
            if (ConversionLogs.Count > 20)
            {
                ConversionLogs.RemoveAt(ConversionLogs.Count - 1);
            }
        }

        private void ExecuteSelectFile()
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png", Multiselect = false };
            if (dlg.ShowDialog() == true)
            {
                SelectedFile = dlg.FileName;
                AddLog($"File selected: {System.IO.Path.GetFileName(SelectedFile)}");
            }
        }

        private async Task ExecuteConvertAsync()
        {
            System.Diagnostics.Debug.WriteLine($"Trying to convert");
            AddLog("Starting conversion process...");
            
            try
            {
                if (!int.TryParse(Year, out int yy) ||
                    !int.TryParse(Month, out int mm) ||
                    !int.TryParse(Day, out int dd) ||
                    !int.TryParse(Hour, out int hh) ||
                    !int.TryParse(Minute, out int min))
                {
                    var errorMsg = "Invalid date/time input.";
                    AddLog($"ERROR: {errorMsg}");
                    StatusMessage = errorMsg;
                    return;
                }

                if (yy < 100) yy += 2000;
                if (IsPM && hh < 12) hh += 12;
                if (!IsPM && hh == 12) hh = 0;

                if (mm < 1 || mm > 12 ||
                    dd < 1 || dd > DateTime.DaysInMonth(yy, mm) ||
                    hh < 0 || hh > 23 ||
                    min < 0 || min > 59)
                {
                    var errorMsg = "Invalid date/time values.";
                    AddLog($"ERROR: {errorMsg}");
                    StatusMessage = errorMsg;
                    return;
                }

                DateTime expiry;
                try
                {
                    expiry = new DateTime(yy, mm, dd, hh, min, 0, DateTimeKind.Local);
                }
                catch
                {
                    var errorMsg = "Invalid date/time values.";
                    AddLog($"ERROR: {errorMsg}");
                    StatusMessage = errorMsg;
                    return;
                }

                var hoursDiff = (int)Math.Ceiling((expiry - DateTime.Now).TotalHours);

                if (hoursDiff <= 0)
                {
                    var errorMsg = "Expiration must be in the future.";
                    AddLog($"ERROR: {errorMsg}");
                    StatusMessage = errorMsg;
                    return;
                }

                if (hoursDiff > 24 * 365 * 5)
                {
                    var errorMsg = "Expiration too far in the future.";
                    AddLog($"ERROR: {errorMsg}");
                    StatusMessage = errorMsg;
                    return;
                }

                AddLog($"Converting image with expiration: {expiry:yyyy-MM-dd HH:mm}");
                
                var ttlPath = await _secureManager.ConvertImageToTtlAsync(SelectedFile, hoursDiff);
                var successMsg = $"SUCCESS: Converted → {System.IO.Path.GetFileName(ttlPath)}";
                AddLog(successMsg);
                StatusMessage = successMsg;
            }
            catch (Exception ex)
            {
                var errorMsg = $"ERROR: Conversion failed - {ex.Message}";
                AddLog(errorMsg);
                StatusMessage = errorMsg;
            }
            System.Diagnostics.Debug.WriteLine($"Convert ran");
        }

        // ---------------- INotify ----------------
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
