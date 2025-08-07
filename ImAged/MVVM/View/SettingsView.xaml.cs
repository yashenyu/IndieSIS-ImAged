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
        }

        private void SelectDownloadPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Forms.FolderBrowserDialog();
            dialog.Description = "Select Download Path";

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                DownloadPathTextBox.Text = dialog.SelectedPath;
            }
        }
    }
}


