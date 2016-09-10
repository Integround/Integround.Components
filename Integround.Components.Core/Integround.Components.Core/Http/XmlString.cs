using System.Globalization;
using System.Text.RegularExpressions;

namespace Integround.Components.Http
{
    public class XmlString
    {
        public static string Encode(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            // First escape backslashes:
            str = str.Replace(@"\", @"\\");

            // Then replace invalid XML characters with unicode sequences:
            var invalidXmlCharactersRegex = new Regex("[^\u0009\u0020-\ufffd]|([\ud800-\udbff](?![\udc00-\udfff]))|((?<![\ud800-\udbff])[\udc00-\udfff])");
            return invalidXmlCharactersRegex.Replace(str, match =>
                string.Format(@"\u{0:x4}", (int)match.Value[0]));
        }

        public static string Decode(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            // Find unicode sequences and replace them with characters:
            var regex = new Regex(@"(\\+)u([0-9A-F]{4})", RegexOptions.IgnoreCase);
            var output = regex.Replace(str, match =>
            {
                var backSlashes = match.Groups[1].Value;

                // If there is uneven number of backslashes, this is escaped
                // -> parse the hex value & convert to char
                // -> only remove one backslash, other are written back to the string
                if (backSlashes.Length % 2 == 1)
                {
                    return string.Concat(backSlashes.Remove(0, 1),
                        ((char)int.Parse(match.Groups[2].Value, NumberStyles.HexNumber)));
                }

                return match.Groups[0].Value;
            });

            // Lastly un-escape backslashes:
            return output.Replace(@"\\", @"\");
        }
    }
}
