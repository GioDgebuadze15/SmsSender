using System.Text;
using System.Web;
using SmsReceiver.AppServices.ParserService;
using SmsReceiver.Data;
using SmsReceiver.Models;

namespace SmsReceiver.AppServices.CheckerService;

public class FineStatusCheckerService
{
    private readonly SemaphoreSlim _gate = new(1);

    private readonly AppDbContext _ctx;
    private readonly string _fineWebsiteBaseUrl;

    public FineStatusCheckerService(AppDbContext ctx, string fineWebsiteBaseUrl)
    {
        _ctx = ctx;
        _fineWebsiteBaseUrl = fineWebsiteBaseUrl;
    }

    public async Task CheckForFineStatus()
    {
        await _gate.WaitAsync();
        var fines = GetFines();
        foreach (var fine in fines)
        {
            if (string.IsNullOrEmpty(fine.ReceiptNumber) || string.IsNullOrEmpty(fine.CarNumber)) continue;
            var encodedReceiptNumber = HttpUtility.UrlEncode(fine.ReceiptNumber, Encoding.UTF8).ToUpper()
                .Replace("0201569", "0193557");

            var url = BuildQuery(fine.CarNumber, encodedReceiptNumber);
            var result = GetFineStatus(url);

            var paid = RegexParser.ParsePaidFineResponse(result);
            var unPaid = RegexParser.ParseUnPaidFineResponse(result);
            fine.FineStatus = paid.Success ? FineStatus.Paid : unPaid.Success ? FineStatus.Unpaid : FineStatus.NotFound;

            await UpdateFineStatus(fine);
        }

        _gate.Release();
    }

    private static string GetFineStatus(string url)
    {
        //Todo: check why i cant use await client.GetAsync() 
        using var client = new HttpClient();
        var response = client.GetAsync(url).Result;
        var json = response.Content.ReadAsStringAsync().Result.Trim();

        return json;
    }

    private string BuildQuery(string carNumber, string receiptNumber)
        =>
            $"{_fineWebsiteBaseUrl}?input_v_1_val={carNumber}&input_v_2_val={receiptNumber}&index_select_val=car_id_penalty_n";

    private IEnumerable<ReceivedSms> GetFines()
        => _ctx.ReceivedSms.Where(x =>
            x.Parsed && x.FineStatus != FineStatus.Paid && x.LastDateOfPayment > DateTimeOffset.Now);

    private async Task UpdateFineStatus(ReceivedSms oldFine)
    {
        var fine = _ctx.ReceivedSms.FirstOrDefault(x => x.Id.Equals(oldFine.Id));
        if (fine == null) return;
        fine.FineStatus = oldFine.FineStatus;
        await _ctx.SaveChangesAsync();
    }
}