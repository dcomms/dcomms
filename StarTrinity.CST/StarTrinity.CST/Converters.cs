using System;
using System.Collections.Generic;
using System.Text;
using Xamarin.Forms;

namespace StarTrinity.CST
{
    public class OppositeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool val = System.Convert.ToBoolean(value);
            return !val;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return !System.Convert.ToBoolean(value);
        }

    }
}
