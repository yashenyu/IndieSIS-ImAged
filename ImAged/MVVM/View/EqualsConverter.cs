using System;
using System.Globalization;
using System.Windows.Data;

namespace ImAged.MVVM.View
{
    // The EqualsConverter checks if a bound value is equal to the ConverterParameter.
    public class EqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Compares the value from the binding with the parameter from the XAML.
            return value?.Equals(parameter) ?? false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ConvertBack is not needed for this scenario.
            return Binding.DoNothing;
        }
    }
}
