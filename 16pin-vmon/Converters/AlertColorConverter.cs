using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace _16pin_vmon.Core.Converters
{
    public class AlertColorConverter : IValueConverter
    {
        /// <summary>
        /// 當 IsVoltageAlert 為 true 時，回傳紅色；否則回傳預設顏色（白色或灰色）。
        /// </summary>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isAlert && isAlert)
            {
                return Brushes.Red; // 警報時顯示紅色
            }

            // 正常時顯示的顏色 (這裡可以根據你的 UI 配色調整)
            return Brushes.White; 
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}