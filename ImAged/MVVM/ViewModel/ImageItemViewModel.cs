using System;
using System.ComponentModel;
using System.IO;

namespace ImAged.MVVM.ViewModel
{
    public class ImageItemViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        public string FilePath { get; }

        private DateTime _expirationDate;
        public DateTime ExpirationDate
        {
            get => _expirationDate;
            private set
            {
                _expirationDate = value;
                OnPropertyChanged(nameof(ExpirationDate));
                OnPropertyChanged(nameof(EstimatedRemainingTime));
            }
        }

        public string EstimatedRemainingTime
        {
            get
            {
                TimeSpan remaining = ExpirationDate - DateTime.Now;
                return remaining < TimeSpan.Zero ? "Expired" : remaining.ToString(@"dd\.hh\:mm\:ss");
            }
        }

        public ImageItemViewModel(string filePath)
        {
            FilePath = filePath;
            ExpirationDate = ExtractExpirationDate(filePath);
        }

        private DateTime ExtractExpirationDate(string imageFilePath)
        {
            // Construct the TTL file name (same basename with .ttl extension)
            string ttlFile = Path.ChangeExtension(imageFilePath, ".ttl");
            if (File.Exists(ttlFile))
            {
                try
                {
                    byte[] ttlBytes = File.ReadAllBytes(ttlFile);
                    if (ttlBytes.Length >= 8)
                    {
                        // TTL file is 8-byte big-endian value representing Unix timestamp (in seconds).
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(ttlBytes);
                        long unixTimestamp = BitConverter.ToInt64(ttlBytes, 0);
                        return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                    }
                }
                catch (Exception)
                {
                    // Log error if you have logging, and fallback.
                }
            }
            return DateTime.MinValue;
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}