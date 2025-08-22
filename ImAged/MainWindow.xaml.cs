using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ImAged
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            StateChanged += MainWindow_StateChanged;
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            UpdateCornerRadius();
        }

        private void UpdateCornerRadius()
        {
            // Get the main border only
            var mainBorder = this.FindName("MainBorder") as Border;

            if (WindowState == WindowState.Maximized)
            {
                // Remove rounded corners from main border only when maximized
                if (mainBorder != null)
                    mainBorder.CornerRadius = new CornerRadius(0);
            }
            else
            {
                // Restore rounded corners on main border when not maximized
                if (mainBorder != null)
                    mainBorder.CornerRadius = new CornerRadius(10);
            }
        }

        private void WindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
                if (Top <= 0)
                {
                    WindowState = WindowState.Maximized;
                }
            }
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}