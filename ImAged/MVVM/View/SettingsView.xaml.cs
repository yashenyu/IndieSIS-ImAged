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
using System.Windows.Forms; 
using System.IO;            // For saving path if needed
using Forms = System.Windows.Forms;

namespace ImAged.MVVM.View
{
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        public SettingsView()
        {
            InitializeComponent();

            DownloadPathTextBox.Text = Properties.Settings.Default.DownloadPath ?? "No Path Selected";
        }

        private void SelectDownloadPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Forms.FolderBrowserDialog();
            dialog.Description = "Select Download Path";

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                string selectedPath = dialog.SelectedPath;

                // Display the selected path
                DownloadPathTextBox.Text = selectedPath;

                // Save to settings
                Properties.Settings.Default.DownloadPath = selectedPath;
                Properties.Settings.Default.Save();
            }
            else
            {
                System.Windows.MessageBox.Show("Selected path does not exist or was cancelled.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}


