using System;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace ImAged.MVVM.Model
{
    public class FileItem : INotifyPropertyChanged
    {
        public string FileName { get; set; }
        public string FileType { get; set; }
        public double FileSize { get; set; } // KB
        public string FilePath { get; set; }
        public DateTime Created { get; set; }
        private string _state; //  (e.g., "Active", "Expired")
        public string State
        {
            get => _state;
            set
            {
                if (!string.Equals(_state, value, StringComparison.Ordinal))
                {
                    _state = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
                }
            }
        }

        public string ImagePath { get; set; }

        private BitmapSource _thumbnail;
        public BitmapSource Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (!Equals(_thumbnail, value))
                {
                    _thumbnail = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
                }
            }
        }

        public bool IsFolder { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }

}
