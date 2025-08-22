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

namespace ImAged.MVVM.View
{

    public partial class ConvertView : UserControl
    {
        public ConvertView()
        {
            InitializeComponent();
        }

        private void DateTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!e.Text.All(char.IsDigit))
            {
                e.Handled = true;
            }
        }

        private void DayTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void DayTextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }
    }
}
