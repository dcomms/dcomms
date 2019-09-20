using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Dcomms.CryptographyTester
{
    public class ColorToBrushConverter : MarkupExtension, IValueConverter
    {
        public ColorToBrushConverter()
        {
        }


        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var val = (System.Drawing.Color)(value);
            return new SolidColorBrush(Color.FromRgb(val.R, val.G, val.B));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
