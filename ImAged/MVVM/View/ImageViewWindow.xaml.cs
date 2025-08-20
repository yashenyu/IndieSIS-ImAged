using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
using System.Linq;

namespace ImAged.MVVM.View
{
    public partial class ImageViewWindow : Window
    {

        public ImageViewWindow(BitmapSource image, string filePath)
        {
            InitializeComponent();

            // enforce minimum window size
            MinWidth = 1080;
            MinHeight = 720;

            // size window to image (at least min) and center on screen
            double desiredW = Math.Max(MinWidth, image.PixelWidth);
            double desiredH = Math.Max(MinHeight, image.PixelHeight);

            Rect wa = SystemParameters.WorkArea;
            desiredW = Math.Min(desiredW, wa.Width);
            desiredH = Math.Min(desiredH, wa.Height);

            Width = desiredW;
            Height = desiredH;

            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;

            // show the bitmap
            FullImage.Source = image;

            // banner
            FileNameText.Text = Path.GetFileName(filePath);

            // optionally store file info if needed later
        }

        // no zoom/scroll logic required anymore

        // Window control buttons
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /* maximize toggle */
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else WindowState = WindowState.Maximized;
        }

        // modify WindowDrag to maximize when snapped top
        private void WindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            DragMove();
            if (Top <= 0)
            {
                WindowState = WindowState.Maximized;
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Normal)
            {
                // pick a window size: max(image native size, min 1080×720) but not larger than screen
                double desiredW = MinWidth;
                double desiredH = MinHeight;

                if (FullImage?.Source is BitmapSource bmp)
                {
                    desiredW = Math.Max(MinWidth, bmp.PixelWidth);
                    desiredH = Math.Max(MinHeight, bmp.PixelHeight);
                }

                // clamp to primary work area
                Rect wa = SystemParameters.WorkArea;
                desiredW = Math.Min(desiredW, wa.Width);
                desiredH = Math.Min(desiredH, wa.Height);

                Width = desiredW;
                Height = desiredH;

                // center on screen
                Left = wa.Left + (wa.Width - Width) / 2;
                Top = wa.Top + (wa.Height - Height) / 2;
            }

            // no extra fit logic necessary – Stretch="Uniform" handles it automatically
        }
    }
}