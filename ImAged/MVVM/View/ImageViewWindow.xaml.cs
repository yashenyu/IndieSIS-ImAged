using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;

namespace ImAged.MVVM.View
{
    public partial class ImageViewWindow : Window
    {
        private double _fitScale = 1.0;
        private const double _maxScale = 5.0;
        private bool _isPanning;
        private Point _panStart;
        private double _startH, _startV;

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

            // Fit image to window once layout is ready
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToWindow));

            // hook events for panning and wheel zoom relative to cursor
            FullImage.MouseWheel += FullImage_MouseWheel;
            FullImage.MouseLeftButtonDown += FullImage_MouseLeftButtonDown;
            FullImage.MouseLeftButtonUp += FullImage_MouseLeftButtonUp;
            FullImage.MouseMove += FullImage_MouseMove;

            ImgScroll.PreviewMouseWheel += ImgScroll_PreviewMouseWheel;
        }

        // prevent ScrollViewer auto-scroll when we are zooming (scale>fit)
        private void ImgScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImageScale.ScaleX > _fitScale + 0.0001)
            {
                e.Handled = true; // we manage panning ourselves
            }
        }

        /* make the image fully visible inside the viewport */
        private void FitToWindow()
        {
            if (FullImage.Source is BitmapSource bmp)
            {
                // available area inside ScrollViewer
                double viewW = ImgScroll.ViewportWidth > 0 ? ImgScroll.ViewportWidth : ImgScroll.ActualWidth;
                double viewH = ImgScroll.ViewportHeight > 0 ? ImgScroll.ViewportHeight : ImgScroll.ActualHeight;

                if (viewW <= 0 || viewH <= 0) return;

                double scale = Math.Min(viewW / bmp.PixelWidth, viewH / bmp.PixelHeight);
                scale = Math.Min(1.0, scale);       // never upscale on initial fit

                _fitScale = scale;

                ZoomSlider.Minimum = _fitScale;
                ZoomSlider.Maximum = _maxScale;
                ZoomSlider.Value = _fitScale;

                CenterImage();

                if (scale < ZoomSlider.Minimum) ZoomSlider.Minimum = scale; // ensure within range

                ZoomSlider.Value = scale;
            }
        }

        /* slider drives the ScaleTransform */
        private void ZoomSlider_ValueChanged(object sender,
                                             RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageScale == null)                // <- first call during InitializeComponent
                return;

            ImageScale.ScaleX = ImageScale.ScaleY = e.NewValue;

            // recenter when zoomed out to fit
            if (ImageScale.ScaleX <= _fitScale + 0.0001)
            {
                CenterImage();
            }
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

        private void FullImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double step = 0.1;
            double delta = e.Delta > 0 ? step : -step;
            double newVal = Math.Max(_fitScale, Math.Min(_maxScale, ZoomSlider.Value + delta));

            // position relative to image
            Point p = e.GetPosition(FullImage);
            double relX = p.X / FullImage.ActualWidth;
            double relY = p.Y / FullImage.ActualHeight;

            // current offsets
            double prevScale = ImageScale.ScaleX;
            ZoomSlider.Value = newVal; // this triggers scale change

            Dispatcher.InvokeAsync(() =>
            {
                double newScale = ImageScale.ScaleX;
                if (newScale == 0) return;

                double viewW = ImgScroll.ViewportWidth;
                double viewH = ImgScroll.ViewportHeight;

                double newImgW = FullImage.ActualWidth * newScale / prevScale;
                double newImgH = FullImage.ActualHeight * newScale / prevScale;

                double targetX = (newImgW * relX) - viewW / 2;
                double targetY = (newImgH * relY) - viewH / 2;

                ImgScroll.ScrollToHorizontalOffset(targetX);
                ImgScroll.ScrollToVerticalOffset(targetY);
            }, DispatcherPriority.Background);

            e.Handled = true;
        }

        private void FullImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImageScale.ScaleX <= _fitScale) return; // no panning when fitted
            _isPanning = true;
            _panStart = e.GetPosition(ImgScroll);
            _startH = ImgScroll.HorizontalOffset;
            _startV = ImgScroll.VerticalOffset;
            FullImage.CaptureMouse();
        }

        private void FullImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            Point pos = e.GetPosition(ImgScroll);
            double dx = pos.X - _panStart.X;
            double dy = pos.Y - _panStart.Y;
            ImgScroll.ScrollToHorizontalOffset(_startH - dx);
            ImgScroll.ScrollToVerticalOffset(_startV - dy);
        }

        private void FullImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning) return;
            _isPanning = false;
            FullImage.ReleaseMouseCapture();
        }

        private void CenterImage()
        {
            ImgScroll.ScrollToHorizontalOffset((FullImage.ActualWidth * ImageScale.ScaleX - ImgScroll.ViewportWidth) / 2);
            ImgScroll.ScrollToVerticalOffset((FullImage.ActualHeight * ImageScale.ScaleY - ImgScroll.ViewportHeight) / 2);
        }

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
                FitToWindow();
            }
        }
    }
}