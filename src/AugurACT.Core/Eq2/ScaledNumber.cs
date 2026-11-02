using System.Globalization;

namespace AugurACT.Core.Eq2
{
    public static class ScaledNumber
    {
        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (text.Length == 0)
            {
                return false;
            }

            var scale = 1d;
            var digits = text.Replace(",", "");
            switch (digits[digits.Length - 1])
            {
                case 'K': scale = 1e3; break;
                case 'M': scale = 1e6; break;
                case 'B': scale = 1e9; break;
                case 'T': scale = 1e12; break;
                case 'Q': scale = 1e15; break;
            }

            if (scale != 1d)
            {
                digits = digits.Substring(0, digits.Length - 1);
            }

            if (!double.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            value = number * scale;
            return true;
        }
    }
}
