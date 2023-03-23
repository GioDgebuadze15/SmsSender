using System.Globalization;

namespace SmsReceiver.AppServices.ParserService;

public class DateTimeParser : CustomParser<DateTimeOffset?>
{
    private readonly string[] _formats =
    {
        "dd/MM/yyyy",
        "yyyy/MM/dd HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
        "yyyy/MM/dd,HH:mm:ss",
        "dd/MM/yyyy,HH:mm:ss",
        "dd/MM/yy,HH:mm:ss"
    };

    public override DateTimeOffset? Parse(string toParse)
    {
        foreach (var format in _formats)
        {
            if (DateTimeOffset.TryParseExact(toParse, format, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var parsedDateTimeOffset))
            {
                return parsedDateTimeOffset;
            }
        }

        return null;
    }
}