using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ImAged.MVVM.View
{
    public partial class ImageViewWindow : Window
    {
        public ImageViewWindow(BitmapSource image, string filePath)
        {
            InitializeComponent();

            // show the bitmap
            FullImage.Source = image;

            // banner
            FileNameText.Text = Path.GetFileName(filePath);

            // info (dimensions / file size / expiry)
            var fi = new FileInfo(filePath);
            string size = $"{fi.Length / 1024:N0} KB";
            string dim = $"{image.PixelWidth} × {image.PixelHeight}px";
            string expiry = "(exp N/A)";
            InfoText.Text = $"{dim}   |   {size}   |   {expiry}";
        }

        /* slider drives the ScaleTransform */
        private void ZoomSlider_ValueChanged(object sender,
                                             RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageScale == null)                // <- first call during InitializeComponent
                return;

            ImageScale.ScaleX = ImageScale.ScaleY = e.NewValue;
        }

        /* mouse-wheel zoom shortcut */
        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double step = 0.1;
            if (e.Delta > 0) ZoomSlider.Value = Math.Min(ZoomSlider.Maximum,
                                                         ZoomSlider.Value + step);
            else ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                                                         ZoomSlider.Value - step);
        }
    }
}