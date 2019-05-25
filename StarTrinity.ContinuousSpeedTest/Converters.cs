using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;


namespace StarTrinity.ContinuousSpeedTest
{
	public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
	{
		public BoolToVisibilityConverter()
		{
			TrueValue = Visibility.Visible;
			FalseValue = Visibility.Collapsed;
		}

		public Visibility TrueValue { get; set; }
		public Visibility FalseValue { get; set; }

		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			bool val = System.Convert.ToBoolean(value);
			return val ? TrueValue : FalseValue;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			return TrueValue.Equals(value) ? true : false;
		}

		public override object ProvideValue(IServiceProvider serviceProvider)
		{
			return this;
		}
	}
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

	public class LogScaleConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value == null) return null;
			double x = System.Convert.ToDouble(value);
			return Math.Log10(x);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			double x = (double)value;
			return Math.Pow(10, x);
		}
	}


    


    public class ColorToBrushConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var color = (System.Drawing.Color)(value);
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
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


    public class SubtMeasurementToUpDownBrushConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var m = (Dcomms.SUBT.SubtMeasurement)(value);
            var color = ((m.AppFlags0 & 0x01) != 0) ? System.Windows.Media.Color.FromArgb(255, 150, 255, 150) : System.Windows.Media.Color.FromArgb(255, 255, 150, 150);
            return new System.Windows.Media.SolidColorBrush(color);
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
    public class SubtMeasurementToUpDownTextConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var m = (Dcomms.SUBT.SubtMeasurement)(value);
            return ((m.AppFlags0 & 0x01) != 0) ? "up" : "down";
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
