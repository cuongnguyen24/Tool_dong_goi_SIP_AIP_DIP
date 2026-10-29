using System;
using System.Globalization;
using System.Windows.Data;

namespace AXE.PackagingTool.Converters;

public class BoolNegationConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value is bool flag && !flag;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value is bool flag && !flag;
	}
}
