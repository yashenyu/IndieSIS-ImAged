using ImAged.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ImAged.MVVM.ViewModel
{
    public class FolderModel : ObservableObject
    {
        public string Name { get; set; }
        public string Info { get; set; }
        public bool IsArchived { get; set; }
        public string ImagePath { get; set; }
        public ICommand FolderClickedCommand { get; }

        private BitmapImage _thumbnail;
        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        public FolderModel()
        {
            FolderClickedCommand = new RelayCommand(ExecuteFolderClick);
        }

        private bool _thumbnailRequested;
        public async void LoadThumbnailAsync()
        {
            if (string.IsNullOrEmpty(ImagePath))
                return;

            if (_thumbnailRequested)
                return;

            _thumbnailRequested = true;
            await Task.Run(() =>
            {
                try
                {
                    using (var stream = new MemoryStream(File.ReadAllBytes(ImagePath)))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Thumbnail = bitmap;
                        });
                    }
                }
                catch (Exception)
                {
                    // Handle image loading errors (e.g., file not found)
                }
            });
        }

        private void ExecuteFolderClick(object obj)
        {
            MessageBox.Show($"Folder clicked: {Name}");
        }
    }
}