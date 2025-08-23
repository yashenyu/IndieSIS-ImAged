using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using ImAged.Core;
using ImAged.Services;
using System.IO;
using System.Text;

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
        private int _currentStep = 1;
        private const int MAX_STEPS = 5;
        private bool _hasConvertedFile = false;
        private string _convertedTtlPath;
        private byte[] _originalFileBytes;
        private byte[] _convertedTtlBytes;

        // ---------------- Properties ----------------
        public ObservableCollection<string> ConversionLogs { get; } = new ObservableCollection<string>();
        public ObservableCollection<FilePart> CurrentStepParts { get; } = new ObservableCollection<FilePart>();
        public ObservableCollection<FilePart> OriginalFileParts { get; } = new ObservableCollection<FilePart>();

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

        public bool HasConvertedFile
        {
            get => _hasConvertedFile;
            set
            {
                _hasConvertedFile = value;
                OnPropertyChanged(nameof(HasConvertedFile));
                OnPropertyChanged(nameof(ShowSimulationContent));
            }
        }

        public bool ShowSimulationContent => HasConvertedFile;

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

        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                _currentStep = value;
                OnPropertyChanged(nameof(CurrentStep));
                OnPropertyChanged(nameof(CurrentStepText));
                UpdateStepParts();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CurrentStepText
        {
            get
            {
                switch (CurrentStep)
                {
                    case 1:
                        return "Step 1: Generate salt and create keys.";
                    case 2:
                        return "Step 2: Create header with expiry timestamp";
                    case 3:
                        return "Step 3: Generate header nonce and authenticate header using AES-GCM";
                    case 4:
                        return "Step 4: Generate body nonce and, authenticate and encrypt payload using AES-GCM";
                    case 5:
                        return "Step 5: Combine all components and generate into ttl file";
                    default:
                        return $"Step {CurrentStep} of {MAX_STEPS}";
                }
            }
        }

        // ---------------- Commands ----------------
        public RelayCommand SelectFileCommand { get; }
        public RelayCommand ToggleAmPmCommand { get; }
        public RelayCommand ConvertCommand { get; }
        public RelayCommand NextStepCommand { get; }
        public RelayCommand PreviousStepCommand { get; }

        // ---------------- Constructor ----------------
        public ConvertViewModel()
        {
            SelectFileCommand = new RelayCommand(_ => ExecuteSelectFile());
            ToggleAmPmCommand = new RelayCommand(_ => IsPM = !IsPM);
            ConvertCommand = new RelayCommand(async _ => await ExecuteConvertAsync(), _ => !string.IsNullOrEmpty(SelectedFile));
            NextStepCommand = new RelayCommand(_ => NextStep(), _ => CurrentStep < MAX_STEPS && HasConvertedFile);
            PreviousStepCommand = new RelayCommand(_ => PreviousStep(), _ => CurrentStep > 1);

            _ = _secureManager.InitializeAsync();
            UpdateStepParts(); // Initialize first step
        }

        // ---------------- Simulation Methods ----------------
        private void NextStep()
        {
            if (CurrentStep < MAX_STEPS)
                CurrentStep++;
        }

        private void PreviousStep()
        {
            if (CurrentStep > 1)
                CurrentStep--;
        }

        private void UpdateStepParts()
        {
            CurrentStepParts.Clear();
            
            if (!HasConvertedFile)
                return;
            
            switch (CurrentStep)
            {
                case 1:
                    // Magic and Salt
                    CurrentStepParts.Add(new FilePart { Name = "Magic", Data = "494d41474544" });
                    CurrentStepParts.Add(new FilePart { Name = "Salt", Data = GetSaltHex() });
                    break;
                    
                case 2:
                    // Magic, Salt, and Header
                    CurrentStepParts.Add(new FilePart { Name = "Magic", Data = "494d41474544" });
                    CurrentStepParts.Add(new FilePart { Name = "Salt", Data = GetSaltHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header", Data = GetHeaderHex() });
                    break;
                    
                case 3:
                    // Magic, Salt, Header Nonce, Header, Header Tag
                    CurrentStepParts.Add(new FilePart { Name = "Magic", Data = "494d41474544" });
                    CurrentStepParts.Add(new FilePart { Name = "Salt", Data = GetSaltHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header Nonce", Data = GetHeaderNonceHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header", Data = GetEncryptedHeaderHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header Tag", Data = GetHeaderTagHex() });
                    break;
                    
                case 4:
                    // Magic, Salt, Header Nonce, Header, Header Tag, Body Nonce, Body
                    CurrentStepParts.Add(new FilePart { Name = "Magic", Data = "494d41474544" });
                    CurrentStepParts.Add(new FilePart { Name = "Salt", Data = GetSaltHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header Nonce", Data = GetHeaderNonceHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header", Data = GetEncryptedHeaderHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header Tag", Data = GetHeaderTagHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Body Nonce", Data = GetBodyNonceHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Body", Data = GetOriginalBodyHex() });
                    break;
                    
                case 5:
                    // Magic, Salt, Header Nonce, Header, Header Tag, Body Nonce, Body Tag, Encrypted Body
                    CurrentStepParts.Add(new FilePart { Name = "Magic", Data = "494d41474544" });
                    CurrentStepParts.Add(new FilePart { Name = "Salt", Data = GetSaltHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header Nonce", Data = GetHeaderNonceHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header", Data = GetEncryptedHeaderHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Header Tag", Data = GetHeaderTagHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Body Nonce", Data = GetBodyNonceHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Body Tag", Data = GetBodyTagHex() });
                    CurrentStepParts.Add(new FilePart { Name = "Encrypted Body", Data = GetEncryptedBodyHex() });
                    break;
            }
        }

        private void UpdateOriginalFileParts()
        {
            OriginalFileParts.Clear();
            
            if (_originalFileBytes == null || _originalFileBytes.Length == 0)
                return;

            // Extract magic number (first 6 bytes)
            var magicBytes = new byte[6];
            Array.Copy(_originalFileBytes, 0, magicBytes, 0, 6);
            var magicHex = BitConverter.ToString(magicBytes).Replace("-", "");
            OriginalFileParts.Add(new FilePart { Name = "Magic Number", Data = magicHex });

            // Show file data with truncation only for large files
            var headerSize = _originalFileBytes.Length - 6;
            if (headerSize > 0)
            {
                var headerBytes = new byte[headerSize];
                Array.Copy(_originalFileBytes, 6, headerBytes, 0, headerSize);
                var headerHex = BitConverter.ToString(headerBytes).Replace("-", "");
                OriginalFileParts.Add(new FilePart { Name = "Complete File Data", Data = TruncateHexData(headerHex, 6000) }); // Only truncate large file data
            }
        }

        // Helper methods to extract TTL file parts - NO truncation for small fields
        private string GetSaltHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 22) return "[Salt Data]";
            var saltBytes = new byte[16];
            Array.Copy(_convertedTtlBytes, 6, saltBytes, 0, 16);
            var hex = BitConverter.ToString(saltBytes).Replace("-", "").ToLower();
            return hex; // Salt is always 32 chars - NO TRUNCATION
        }

        private string GetHeaderHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 30) return "[Header Data]";
            var headerBytes = new byte[8];
            Array.Copy(_convertedTtlBytes, 34, headerBytes, 0, 8);
            var hex = BitConverter.ToString(headerBytes).Replace("-", "").ToLower();
            return hex; // Header is always 16 chars - NO TRUNCATION
        }

        private string GetHeaderNonceHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 30) return "[Nonce Bytes]";
            var nonceBytes = new byte[12];
            Array.Copy(_convertedTtlBytes, 22, nonceBytes, 0, 12);
            var hex = BitConverter.ToString(nonceBytes).Replace("-", "").ToLower();
            return hex; // Nonce is always 24 chars - NO TRUNCATION
        }

        private string GetEncryptedHeaderHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 42) return "[Encrypted Header]";
            var headerBytes = new byte[8];
            Array.Copy(_convertedTtlBytes, 34, headerBytes, 0, 8);
            var hex = BitConverter.ToString(headerBytes).Replace("-", "").ToLower();
            return hex; // Header is always 16 chars - NO TRUNCATION
        }

        private string GetHeaderTagHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 50) return "[Authentication Tag]";
            var tagBytes = new byte[16];
            Array.Copy(_convertedTtlBytes, 42, tagBytes, 0, 16);
            var hex = BitConverter.ToString(tagBytes).Replace("-", "").ToLower();
            return hex; // Tag is always 32 chars - NO TRUNCATION
        }

        private string GetBodyNonceHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 62) return "[Body Nonce Bytes]";
            var nonceBytes = new byte[12];
            Array.Copy(_convertedTtlBytes, 50, nonceBytes, 0, 12);
            var hex = BitConverter.ToString(nonceBytes).Replace("-", "").ToLower();
            return hex; // Nonce is always 24 chars - NO TRUNCATION
        }

        private string GetOriginalBodyHex()
        {
            if (_originalFileBytes == null) return "[Original File Bytes]";
            var hex = BitConverter.ToString(_originalFileBytes).Replace("-", "").ToLower();
            return TruncateHexData(hex, 6000); // Only truncate large original body data
        }

        private string GetBodyTagHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 78) return "[Body Auth Tag]";
            var tagBytes = new byte[16];
            Array.Copy(_convertedTtlBytes, 62, tagBytes, 0, 16);
            var hex = BitConverter.ToString(tagBytes).Replace("-", "").ToLower();
            return hex; // Tag is always 32 chars - NO TRUNCATION
        }

        private string GetEncryptedBodyHex()
        {
            if (_convertedTtlBytes == null || _convertedTtlBytes.Length < 78) return "[Encrypted File Data]";
            var bodySize = _convertedTtlBytes.Length - 78;
            var bodyBytes = new byte[bodySize];
            Array.Copy(_convertedTtlBytes, 78, bodyBytes, 0, bodySize);
            var hex = BitConverter.ToString(bodyBytes).Replace("-", "").ToLower();
            return TruncateHexData(hex, 6000); // Only truncate large encrypted body data
        }

        private string TruncateHexData(string hexData, int maxChars)
        {
            if (string.IsNullOrEmpty(hexData))
                return hexData;

            if (hexData.Length <= maxChars)
                return hexData;

            return hexData.Substring(0, maxChars) + "...";
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
                
                // Reset simulation state when new file is selected
                HasConvertedFile = false;
                _originalFileBytes = null;
                _convertedTtlBytes = null;
                CurrentStep = 1;
                OriginalFileParts.Clear();
                CurrentStepParts.Clear();
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
                
                // Read original file bytes for simulation
                _originalFileBytes = File.ReadAllBytes(SelectedFile);
                UpdateOriginalFileParts();
                
                var ttlPath = await _secureManager.ConvertImageToTtlAsync(SelectedFile, hoursDiff);
                
                // Read converted TTL file bytes for simulation
                _convertedTtlPath = ttlPath;
                _convertedTtlBytes = File.ReadAllBytes(ttlPath);
                
                // Enable simulation
                HasConvertedFile = true;
                CurrentStep = 1;
                UpdateStepParts();
                
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

    // ---------------- FilePart Model ----------------
    public class FilePart
    {
        public string Name { get; set; }
        public string Data { get; set; }
    }
}
