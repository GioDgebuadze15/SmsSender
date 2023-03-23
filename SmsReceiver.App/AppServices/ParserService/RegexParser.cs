using System.Text.RegularExpressions;

namespace SmsReceiver.AppServices.ParserService;

public static class RegexParser
{
    private static readonly Regex RegexForSms = new(
        @"(?:\+CMGL: )(?<index>[\d]{1,2})[,]+""[\w ]+"",""(?<sender>[+\w .]*)"","""",""(?<time>[+\w\/ :,]*)""(?<text>(?s).*?)(?:\r?\n){2}",
        RegexOptions.Singleline);

    private static readonly Regex RegexForFine = new(
        @": (?<carNumber>[\d\w\-_]+),\s?[\w]+ [\w]+[\w-]+[^\d](?<article>[\d-\w ()]*)[ .\w]+[:](?<street>[\w\d . ,\-#:]+),\s?tarighi[ :]+(?<time>[+\w\/ :]*),\s?[a-zA-Z: ]+\:\s?(?<receiptNumber>[\w]+),\s?[a-zA-Z: ]+\:\s?(?<amount>[\d]+).+chabarebidan\s?(?<term>[\d]+)",
        RegexOptions.Singleline);

    private static readonly Regex RegexForReminder = new(
        @"qvitris (?<receiptNumber>[\w]+)[a-zA-Z ]+(?<lastDateOfPayment>[\d.]+)", RegexOptions.Compiled);

    private static readonly Regex RegexForDeviceResponse = new(
        @"(?:\+CMGL: )(?<index>[\d]{1,2})[,]+""[\w ]+"",""(?<sender>[+\w .]*)"","""",""(?<time>[+\w\/ :,]*)""(?<text>(?s).*?)(?:\r?\n){2}",
        RegexOptions.Compiled);

    private static readonly Regex PaidFine = new(@"გადახდილია", RegexOptions.Compiled);

    private static readonly Regex UnPaidFine = new(@"გადაუხდელია", RegexOptions.Compiled);


    public static MatchCollection ParseSms(string text)
        => RegexForSms.Matches(text);

    public static Match ParseSmsForFine(string text)
        => RegexForFine.Match(text);

    public static Match ParseSmsForReminder(string text)
        => RegexForReminder.Match(text);

    public static Match ParseSmsForDeviceResponse(string text)
        => RegexForDeviceResponse.Match(text);

    public static Match ParsePaidFineResponse(string text)
        => PaidFine.Match(text);

    public static Match ParseUnPaidFineResponse(string text)
        => UnPaidFine.Match(text);
}