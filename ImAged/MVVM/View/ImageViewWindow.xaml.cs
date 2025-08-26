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
        private const double ZoomFactor = 1.2; // step factor per wheel delta
        private const double MinScale = 1;     // don't allow smaller than original fit
        private const double MaxScale = 15;    // arbitrary upper bound

        // fields for drag panning
        private bool _isDragging;
        private Point _dragStart;
        private double _startX;
        private double _startY;

        // Static event for window lifecycle tracking
        public static event EventHandler<ImageViewWindowEventArgs> WindowOpened;
        public static event EventHandler<ImageViewWindowEventArgs> WindowClosed;

        public string FilePath { get; private set; }

        public ImageViewWindow(BitmapSource image, string filePath)
        {
            InitializeComponent();
            FilePath = filePath;

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

            // status bar info
            try
            {
                var fi = new FileInfo(filePath);
                string sizeStr = fi.Exists ? (fi.Length / 1024d / 1024d).ToString("0.##") + " MB" : "N/A";
                string resolutionStr = $"{image.PixelWidth}×{image.PixelHeight}";
                string dateStr = fi.Exists ? fi.CreationTime.ToString("yyyy-MM-dd HH:mm") : "N/A";
                StatusText.Text = $"{sizeStr}   •   {resolutionStr}   •   {dateStr}";
            }
            catch { /* ignore */ }

            // Notify that window was opened
            WindowOpened?.Invoke(this, new ImageViewWindowEventArgs(filePath, this));

            // Handle window closing
            this.Closed += (sender, e) =>
            {
                WindowClosed?.Invoke(this, new ImageViewWindowEventArgs(filePath, this));
            };

            // optionally store file info if needed later
        
            // init transforms (defined in XAML)
            if (ImageScaleTransform != null)
            {
                ImageScaleTransform.ScaleX = 1;
                ImageScaleTransform.ScaleY = 1;
            }
        }

        // handle wheel over image for zoom
        private void FullImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (FullImage.Source == null) return;

            // determine zoom center relative to image
            Point cursorPosition = e.GetPosition(FullImage);

            double scale = e.Delta > 0 ? ZoomFactor : 1 / ZoomFactor;

            // compute new scale within bounds
            double newScale = ImageScaleTransform.ScaleX * scale;
            newScale = Math.Max(MinScale, Math.Min(MaxScale, newScale));

            // if trying to zoom out beyond min just clamp and recentre
            scale = newScale / ImageScaleTransform.ScaleX;

            // apply scaling around cursor
            var group = FullImage.RenderTransform as TransformGroup;
            if (group == null) return; // safety

            // translate cursor point to origin, scale, then translate back
            ImageTranslateTransform.X -= cursorPosition.X;
            ImageTranslateTransform.Y -= cursorPosition.Y;

            ImageTranslateTransform.X *= scale;
            ImageTranslateTransform.Y *= scale;

            ImageScaleTransform.ScaleX = newScale;
            ImageScaleTransform.ScaleY = newScale;

            // moved clamping to after final translation
            ImageTranslateTransform.X += cursorPosition.X;
            ImageTranslateTransform.Y += cursorPosition.Y;

            ClampTranslation();

            // if we reached minimum scale reset translation (fit view)
            if (Math.Abs(ImageScaleTransform.ScaleX - MinScale) < 0.001)
            {
                ImageTranslateTransform.X = 0;
                ImageTranslateTransform.Y = 0;
            }

            e.Handled = true;
        }

        // ---------------- Drag pan logic ----------------
        private void FullImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImageScaleTransform.ScaleX <= MinScale + 0.001) return; // don't pan when image fits

            _isDragging = true;
            _dragStart = e.GetPosition(this);
            _startX = ImageTranslateTransform.X;
            _startY = ImageTranslateTransform.Y;

            FullImage.CaptureMouse();
            e.Handled = true;
        }

        private void FullImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            FullImage.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void FullImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point current = e.GetPosition(this);
            Vector delta = current - _dragStart;

            ImageTranslateTransform.X = _startX + delta.X;
            ImageTranslateTransform.Y = _startY + delta.Y;

            ClampTranslation();
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

        // maintain vertical clipping (allow horizontal overflow)
        private void ImageHost_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateImageHostClip();
        }

        private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateImageHostClip();
        }

        private void UpdateImageHostClip()
        {
            if (ImageHost == null) return;
            double extra = 20000; // large value to cover horizontal overflow
            ImageHost.Clip = new RectangleGeometry(new Rect(-extra/2, 0, ImageHost.ActualWidth + extra, ImageHost.ActualHeight));
        }

        private void ClampTranslation()
        {
            if (FullImage.Source == null) return;
            double hostW = ImageContainer.ActualWidth > 0 ? ImageContainer.ActualWidth : ImageHost.ActualWidth;
            double hostH = ImageContainer.ActualHeight > 0 ? ImageContainer.ActualHeight : ImageHost.ActualHeight;

            double imgW = FullImage.ActualWidth * ImageScaleTransform.ScaleX;
            double imgH = FullImage.ActualHeight * ImageScaleTransform.ScaleY;

            // if image smaller than viewport, center it (translation towards 0)
            double minX, maxX, minY, maxY;
            if (imgW <= hostW)
            {
                minX = maxX = (hostW - imgW) / 2.0;
            }
            else
            {
                minX = hostW - imgW;
                maxX = 0;
            }

            if (imgH <= hostH)
            {
                minY = maxY = (hostH - imgH) / 2.0;
            }
            else
            {
                minY = hostH - imgH;
                maxY = 0;
            }

            ImageTranslateTransform.X = Math.Max(minX, Math.Min(maxX, ImageTranslateTransform.X));
            ImageTranslateTransform.Y = Math.Max(minY, Math.Min(maxY, ImageTranslateTransform.Y));
        }
    }
}

// Move the EventArgs class outside the namespace to make it globally accessible
public class ImageViewWindowEventArgs : EventArgs
{
    public string FilePath { get; }
    public ImAged.MVVM.View.ImageViewWindow Window { get; }

    public ImageViewWindowEventArgs(string filePath, ImAged.MVVM.View.ImageViewWindow window)
    {
        FilePath = filePath;
        Window = window;
    }
}