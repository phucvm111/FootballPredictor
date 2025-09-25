using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace FootballPredictor
{
    public class ResultToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim().ToUpperInvariant();
            return s switch
            {
                "W" => new SolidColorBrush(Color.FromRgb(46, 204, 113)), // xanh
                "D" => new SolidColorBrush(Color.FromRgb(255, 255, 255)), // trắng
                "L" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),  // đỏ
                _ => new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

