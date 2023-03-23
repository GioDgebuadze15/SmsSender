namespace SmsReceiver.AppServices.ParserService;

public class IntegerParser : CustomParser<int?>
{
    public override int? Parse(string toParse)
    {
        if (int.TryParse(toParse, out var parsedNumber))
        {
            return parsedNumber;
        }

        return null;
    }
}