<<<<<<< Updated upstream
﻿using System;
=======
using System;
>>>>>>> Stashed changes
using System.Globalization;
using System.Windows.Data;

namespace ImAged.MVVM.View
{
<<<<<<< Updated upstream
    // The EqualsConverter checks if a bound value is equal to the ConverterParameter.
=======
>>>>>>> Stashed changes
    public class EqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
<<<<<<< Updated upstream
            // Compares the value from the binding with the parameter from the XAML.
            return value?.Equals(parameter) ?? false;
=======
            return value != null && value.Equals(parameter);
>>>>>>> Stashed changes
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
<<<<<<< Updated upstream
            // ConvertBack is not needed for this scenario.
            return Binding.DoNothing;
        }
    }
}
=======
            if ((bool)value)
                return parameter;
            return Binding.DoNothing;
        }
    }
}
>>>>>>> Stashed changes
