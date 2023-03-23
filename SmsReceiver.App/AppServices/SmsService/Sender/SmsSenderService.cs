using System.Net;
using System.Text;
using Newtonsoft.Json;
using SmsReceiver.Data;
using SmsReceiver.Models;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace SmsReceiver.AppServices.SmsService.Sender;

public class SmsSenderService : ISmsSenderService
{
    private readonly SemaphoreSlim _gate = new(1);

    private readonly AppDbContext _ctx;
    private readonly string _smsSenderApiBaseUrl;
    private readonly string _smsSenderUsername;
    private readonly string _smsSenderPassword;
    private readonly string _myGpsNumbers;
    private readonly string _wissolNumbers;
    private readonly string _geliosApiBaseUrl;
    private readonly string _geliosUsername;
    private readonly string _geliosPassword;

    public SmsSenderService(AppDbContext ctx, string smsSenderApiBaseUrl, string smsSenderUsername,
        string smsSenderPassword, string myGpsNumbers, string wissolNumbers,
        string geliosApiBaseUrl, string geliosUsername, string geliosPassword)
    {
        _ctx = ctx;
        _smsSenderApiBaseUrl = smsSenderApiBaseUrl;
        _smsSenderUsername = smsSenderUsername;
        _smsSenderPassword = smsSenderPassword;
        _myGpsNumbers = myGpsNumbers;
        _wissolNumbers = wissolNumbers;
        _geliosApiBaseUrl = geliosApiBaseUrl;
        _geliosUsername = geliosUsername;
        _geliosPassword = geliosPassword;
    }

    [Obsolete("Obsolete")]
    public async Task Run()
    {
        await _gate.WaitAsync();
        var result = GetNotSentSms();
        foreach (var sms in result)
        {
            var sentSuccessfully = await SendSms(sms);
            if (!sentSuccessfully) continue;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Sms has been successfully sent");
            await UpdateSentStatus(sms);
            Console.WriteLine("Sms status has been successfully updated");
            Console.ResetColor();
        }

        _gate.Release();
    }


    private IEnumerable<ReceivedSms> GetNotSentSms()
        =>
            _ctx.ReceivedSms
                .Where(x => x.SmsStatus == SmsStatus.Fine || x.SmsStatus == SmsStatus.Reminder)
                .Where(x => x.FinishStatus == SmsFinishStatus.Finished)
                .Where(x => x.Sent == false)
                .AsEnumerable();

    [Obsolete("Obsolete")]
    private async Task<bool> SendSms(ReceivedSms sms)
    {
        if (string.IsNullOrEmpty(sms.Text) || string.IsNullOrEmpty(sms.CarNumber)) return false;
        var url = BuildGeliosUrl();
        var myGpsCars = GetCarsFromGelios(url);
        if (myGpsCars == null) return false;

        var smsPostData = BuildSmsPostData(sms.Text, myGpsCars.Any(x =>
            x.name.ToUpper().Replace("-", "").Replace("_", "").Replace(" ", "")
                .Contains(sms.CarNumber))
            ? _myGpsNumbers
            : _wissolNumbers);

        var status = await SendSmsToNumber(smsPostData);
        return status;
    }

    private string BuildGeliosUrl()
        => $"{_geliosApiBaseUrl}login={_geliosUsername}&pass={_geliosPassword}&svc=get_units&params=%7B%7D";

    private string BuildSmsPostData(string text, string numbers)
        =>
            $"username={_smsSenderUsername}&password={_smsSenderPassword}&brand=2&numbers={numbers}&text={text}&unicode=1";


    [Obsolete("Obsolete")]
    private async Task<bool> SendSmsToNumber(string smsPostData)
    {
        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };


        var request = (HttpWebRequest) WebRequest.Create(_smsSenderApiBaseUrl);
        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded";

        var postDataBytes = Encoding.UTF8.GetBytes(smsPostData);
        request.ContentLength = postDataBytes.Length;
        await using (var stream = request.GetRequestStream())
        {
            await stream.WriteAsync(postDataBytes);
        }

        var response = (HttpWebResponse) request.GetResponse();
        var responseString = await new StreamReader(response.GetResponseStream()).ReadToEndAsync();

        var result = JsonSerializer.Deserialize<ResponseStatus>(responseString);

        if (result is not {success: false}) return result?.success ?? false;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(result.error.description);
        Console.ResetColor();

        return result.success;
    }

    private static IEnumerable<CarModel>? GetCarsFromGelios(string url)
    {
        using var client = new HttpClient();
        var response = client.GetAsync(url).Result;
        var jsonString = response.Content.ReadAsStringAsync().Result;
        var result = JsonConvert.DeserializeObject<List<CarModel>>(jsonString);
        return result;
    }

    private async Task UpdateSentStatus(ReceivedSms sms)
    {
        var result = _ctx.ReceivedSms.FirstOrDefault(x => x.Id.Equals(sms.Id));
        result!.Sent = true;
        await _ctx.SaveChangesAsync();
    }
}