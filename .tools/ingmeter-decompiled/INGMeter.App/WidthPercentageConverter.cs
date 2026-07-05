using System;
using System.Globalization;
using System.Windows.Data;

namespace INGMeter.App;

public class WidthPercentageConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		if (values.Length >= 2 && values[0] is double num && values[1] is double num2)
		{
			if (double.IsNaN(num) || double.IsInfinity(num) || double.IsNaN(num2) || double.IsInfinity(num2))
			{
				return 0.0;
			}
			double num3 = Math.Clamp(num, 0.0, 100.0);
			return Math.Max(0.0, num2) * (num3 / 100.0);
		}
		return 0.0;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
