using System;
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

                return $"{System.IO.Path.GetFileName(SelectedFile)}";
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
        private void ExecuteSelectFile()
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png", Multiselect = false };
            if (dlg.ShowDialog() == true)
            {
                SelectedFile = dlg.FileName;
            }
        }

        private async Task ExecuteConvertAsync()
        {
            System.Diagnostics.Debug.WriteLine($"Trying to convert");
            try
            {
                if (!int.TryParse(Year, out int yy) ||
                    !int.TryParse(Month, out int mm) ||
                    !int.TryParse(Day, out int dd) ||
                    !int.TryParse(Hour, out int hh) ||
                    !int.TryParse(Minute, out int min))
                {
                    StatusMessage = "Invalid date/time input.";
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
                    StatusMessage = "Invalid date/time values.";
                    return;
                }

                DateTime expiry;
                try
                {
                    expiry = new DateTime(yy, mm, dd, hh, min, 0, DateTimeKind.Local);
                }
                catch
                {
                    StatusMessage = "Invalid date/time values.";
                    return;
                }

                var hoursDiff = (int)Math.Ceiling((expiry - DateTime.Now).TotalHours);

                if (hoursDiff <= 0)
                {
                    StatusMessage = "Expiration must be in the future.";
                    return;
                }

                if (hoursDiff > 24 * 365 * 5)
                {
                    StatusMessage = "Expiration too far in the future.";
                    return;
                }

                var ttlPath = await _secureManager.ConvertImageToTtlAsync(SelectedFile, hoursDiff);
                StatusMessage = $"Converted → {ttlPath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            System.Diagnostics.Debug.WriteLine($"Convert ran");
        }

        // ---------------- INotify ----------------
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
