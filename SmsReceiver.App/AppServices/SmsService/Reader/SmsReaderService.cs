using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using SmsReceiver.AppServices.ParserService;
using SmsReceiver.Data;
using SmsReceiver.Models;

namespace SmsReceiver.AppServices.SmsService.Reader;

public class SmsReaderService : ISmsReaderService
{
    private readonly SemaphoreSlim _gate = new(1);
    private string? PortName { get; set; }
    private const int BaudRate = 57600;

    private readonly AppDbContext _ctx;
    private readonly SmsDbContext _smsCtx;
    private readonly CustomParser<DateTimeOffset?> _dateTimeParser = new DateTimeParser();
    private readonly CustomParser<int?> _integerParser = new IntegerParser();

    public SmsReaderService(AppDbContext ctx, SmsDbContext smsCtx)
    {
        _ctx = ctx;
        _smsCtx = smsCtx;
    }

    public async Task Run()
    {
        await _gate.WaitAsync();
        var serialPort = Setup();
        if (serialPort == null) return;

        await ReadSms(serialPort);
        serialPort.Close();
        ConsoleLog("Port has successfully closed", ConsoleColor.Green);
        _gate.Release();
    }


    private static bool ExecuteCommand(SerialPort serialPort, string command, int timeout)
    {
        var message = command + Environment.NewLine;
        ConsoleLog($"Message: {message.Trim()}");
        serialPort.Write(message);
        Thread.Sleep(timeout);

        var response = serialPort.ReadExisting();
        if (!response.Contains("OK"))
        {
            ConsoleLog("Response: " + response.Trim());
            return false;
        }

        ConsoleLog("Response: " + response.Trim());
        return true;
    }

    private static string ExecuteCommandWithResponse(SerialPort serialPort, string command, int timeout)
    {
        var message = command + Environment.NewLine;
        ConsoleLog($"Message: {message.Trim()}");
        serialPort.Write(message);
        Thread.Sleep(timeout);

        var response = serialPort.ReadExisting();
        return response.Trim();
    }

    private async Task ReadSms(SerialPort serialPort)
    {
        var response = ExecuteCommandWithResponse(serialPort, "AT+CMGL=\"ALL\"", 30 * 1000);
        ConsoleLog("Response Size: " + response.Length, ConsoleColor.Blue);

        var matches = RegexParser.ParseSms(response);
        if (matches.Count < 1)
            ConsoleLog("No sms found", ConsoleColor.Yellow);

        var incorrectReceivedSmsText = new IncorrectReceivedSms();
        foreach (Match match in matches)
        {
            if (!match.Success) continue;

            var index = match.Groups["index"].Value.Trim();
            var sender = match.Groups["sender"].Value.Trim();
            var time = match.Groups["time"].Value.Trim();
            var text = match.Groups["text"].Value.Trim();

            var sms = ParseSms(sender, time, text);
            if (!string.IsNullOrEmpty(sms.Text))
            {
                sms = ParseSmsText(sms);
            }

            var condition = (sender.Equals("POLICE") || sender.Equals("SK JARIMA") || sender.Equals("VIDEOJARIMA")) &&
                            sms.Parsed == false;
            if (condition)
            {
                incorrectReceivedSmsText.Indexes.Add(index);
                incorrectReceivedSmsText.Sender = sender;
                incorrectReceivedSmsText.Time = time;
                incorrectReceivedSmsText.Text.Append(text);
                continue;
            }

            var id = await SaveSmsIntoDatabase(sms);
            ConsoleLog("Message has successfully saved into database");
            await DeleteSmsInPhone(serialPort, index, id);
            ConsoleLog("Message has successfully removed from phone");
        }

        if (incorrectReceivedSmsText.Text.Length > 1)
        {
            var hexString = incorrectReceivedSmsText.Text.ToString().Replace("00", "");

            var bytes = Enumerable.Range(0, hexString.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hexString.Substring(x, 2), 16))
                .ToArray();
            var decodedString = Encoding.ASCII.GetString(bytes).Replace("?", "").Replace("\u0010", "")
                .Replace("\u0013", "");

            var sender = incorrectReceivedSmsText.Sender!;
            var time = incorrectReceivedSmsText.Time ?? "";

            var sms = ParseSms(sender, time, decodedString);
            if (!string.IsNullOrEmpty(sms.Text))
            {
                sms = ParseSmsText(sms);
            }

            if (!string.IsNullOrEmpty(sms.Text) && !sms.Text.Contains("Tkveni") &&
                (sms.Text.Contains("032 293 44 44") || sms.Text.Contains("0322 41 42 42")))
            {
                await UpdatePreviousSmsText(sms);
                ConsoleLog("Message text has successfully updated", ConsoleColor.Yellow);
                foreach (var index in incorrectReceivedSmsText.Indexes)
                {
                    await DeleteSmsInPhone(serialPort, index);
                    ConsoleLog("Message has successfully removed from phone");
                }

                return;
            }

            var id = await SaveSmsIntoDatabase(sms);
            ConsoleLog("Message has successfully saved into database", ConsoleColor.Yellow);
            foreach (var index in incorrectReceivedSmsText.Indexes)
            {
                await DeleteSmsInPhone(serialPort, index, id);
                ConsoleLog("Message has successfully removed from phone");
            }
        }

        ConsoleLog("####################################");
        ConsoleLog("Program has successfully finished", ConsoleColor.Cyan);
        // ConsoleLog("Press any key to close...");
        ConsoleLog("####################################");
        // Console.ReadKey();
    }


    private ReceivedSms ParseSms(string sender, string time, string text)
        => new()
        {
            Sender = sender,
            ReceivedDate = _dateTimeParser.Parse(time.Split('+')[0]),
            Text = text,
        };

    private ReceivedSms ParseSmsText(ReceivedSms sms)
    {
        if (string.IsNullOrEmpty(sms.Text)) return sms;
        var matchForFine = RegexParser.ParseSmsForFine(sms.Text);
        if (matchForFine.Success)
        {
            ParseMatchedTextToSms(ref sms, matchForFine);
            return sms;
        }

        var matchForReminder = RegexParser.ParseSmsForReminder(sms.Text);
        if (!matchForReminder.Success)
        {
            ConsoleLog("Cannot parse sms text");
            return sms;
        }

        sms.ReceiptNumber = matchForReminder.Groups["receiptNumber"].Value.Trim();
        sms.Parsed = true;
        sms.SmsStatus = SmsStatus.Reminder;
        var lastDateOfPayment = matchForReminder.Groups["lastDateOfPayment"].Value.Trim().Replace(".", "/");
        if (lastDateOfPayment.EndsWith("/"))
        {
            sms.LastDateOfPayment = _dateTimeParser.Parse(lastDateOfPayment[..^1]);
        }

        if (string.IsNullOrEmpty(sms.ReceiptNumber)) return sms;

        FillRemainderSms(ref sms);
        return sms;
    }

    private async Task DeleteSmsInPhone(SerialPort serialPort, string index, int id = 0)
    {
        var numberIndex = _integerParser.Parse(index);
        if (numberIndex == null) return;
        if (!ExecuteCommand(serialPort, $"AT+CMGD={numberIndex}", 1000)) return;
        if (id == 0) return;
        await UpdateDeletedStatus(id);
    }

    private void DeleteDeviceResponseInPhone(SerialPort serialPort, string index)
    {
        var numberIndex = _integerParser.Parse(index);
        if (numberIndex == null) return;
        ExecuteCommand(serialPort, $"AT+CMGD={numberIndex}", 500);
    }

    private static void DeleteAllSmsInPhone(SerialPort serialPort)
        =>
            ExecuteCommand(serialPort, "AT+QMGDA=\"DEL ALL\"", 1000);


    private void ParseMatchedTextToSms(ref ReceivedSms sms, Match match)
    {
        sms.CarNumber = match.Groups["carNumber"].Value.Trim();
        sms.Article = match.Groups["article"].Value.Trim();
        sms.Street = match.Groups["street"].Value.Trim();
        sms.DateOfFine = _dateTimeParser.Parse(match.Groups["time"].Value.Trim());
        sms.ReceiptNumber = match.Groups["receiptNumber"].Value.Trim();
        sms.Amount = _integerParser.Parse(match.Groups["amount"].Value.Trim());
        sms.Term = _integerParser.Parse(match.Groups["term"].Value.Trim());
        sms.Parsed = true;
        sms.SmsStatus = SmsStatus.Fine;
        sms.FinishStatus = SmsFinishStatus.NotFinished;

        if (sms.DateOfFine != null && sms.Term != null)
        {
            sms.LastDateOfPayment = sms.DateOfFine.Value.AddDays(sms.Term.Value);
        }

        sms.ReceiptNumber = sms.Sender switch
        {
            "SK JARIMA" => $"სკ{sms.ReceiptNumber}",
            "VIDEOJARIMA" => $"ვჯ{sms.ReceiptNumber}",
            _ => sms.ReceiptNumber
        };

        if (sms.Text!.Contains("Tkveni") && (sms.Text.Contains("032 293 44 44") || sms.Text.Contains("0322 41 42 42")))
        {
            sms.FinishStatus = SmsFinishStatus.Finished;
        }
    }

    private void FillRemainderSms(ref ReceivedSms sms)
    {
        var fineSms = GetSmsByReceiptNumber(sms.ReceiptNumber!);
        if (fineSms == null) return;
        sms.CarNumber = fineSms.CarNumber;
        sms.Article = fineSms.Article;
        sms.Street = fineSms.Street;
        sms.DateOfFine = fineSms.DateOfFine;
        sms.Amount = fineSms.Amount;
        sms.Term = fineSms.Term;
        sms.Parsed = true;


        if (sms.DateOfFine != null && sms.Term != null && sms.LastDateOfPayment == null)
        {
            sms.LastDateOfPayment = sms.DateOfFine.Value.AddDays(sms.Term.Value);
        }
    }

    private ReceivedSms? GetSmsByReceiptNumber(string receiptNumber)
        => _ctx.ReceivedSms
            .AsEnumerable()
            .FirstOrDefault(x =>
                x.ReceiptNumber != null &&
                x.ReceiptNumber.Equals(receiptNumber, StringComparison.InvariantCultureIgnoreCase));

    private async Task UpdateDeletedStatus(int id)
    {
        var sms = _ctx.ReceivedSms.FirstOrDefault(x => x.Id.Equals(id));
        if (sms != null) sms.Deleted = true;
        await _ctx.SaveChangesAsync();
    }

    private async Task<int> SaveSmsIntoDatabase(ReceivedSms sms)
    {
        _ctx.Add(sms);
        await _ctx.SaveChangesAsync();
        return sms.Id;
    }

    public async Task SendSmsToDevice()
    {
        await _gate.WaitAsync();
        var serialPort = Setup();
        if (serialPort == null) return;


        await Send(serialPort);
        serialPort.Close();
        ConsoleLog("Port has successfully closed", ConsoleColor.Green);
        _gate.Release();
    }

    private async Task Send(SerialPort serialPort)
    {
        var toSend = GetSentSms();
        foreach (var sms in toSend.Where(sms =>
                     !string.IsNullOrEmpty(sms.MobileNumber) && !string.IsNullOrEmpty(sms.Command)))
        {
            var mobileNumber = sms.MobileNumber.Replace("995", "");
            ExecuteCommand(serialPort, $"AT+CMGS=\"{mobileNumber}\"", 200);
            ExecuteCommand(serialPort, $"{sms.Imei} {sms.Command}", 200);
            serialPort.Write(new byte[] {0x1A}, 0, 1);
            Thread.Sleep(500);

            await UpdateSmsSent(sms, DeviceSmsStatus.Processing);
            var responseBuff = ExecuteCommandWithResponse(serialPort, "AT+CMGL=\"ALL\"", 60 * 1000);
            if (responseBuff.Contains("CMTI"))
            {
                var response = ExecuteCommandWithResponse(serialPort, "AT+CMGL=\"ALL\"", 30 * 1000);
                var matches = RegexParser.ParseSms(response);
                if (matches.Count < 1)
                {
                    await UpdateSmsSent(sms, DeviceSmsStatus.Failed);
                    ConsoleLog("No sms found", ConsoleColor.Yellow);
                }

                foreach (Match match in matches)
                {
                    var index = match.Groups["index"].Value.Trim();
                    var sender = match.Groups["sender"].Value.Trim();
                    var time = match.Groups["time"].Value.Trim();
                    sms.ResponseText = match.Groups["text"].Value.Trim();

                    if (!sender.Contains(mobileNumber)) continue;

                    await UpdateSmsSent(sms, DeviceSmsStatus.Successful);
                    DeleteDeviceResponseInPhone(serialPort, index);
                    ConsoleLog("Message has successfully removed from phone");
                }
            }
            else
            {
                await UpdateSmsSent(sms, DeviceSmsStatus.Failed);
            }
        }
    }

    private IEnumerable<SmsSent> GetSentSms()
        => _smsCtx.SmsSent.Where(x => x.SmsStatus == DeviceSmsStatus.NotSent).ToList();

    private async Task UpdateSmsSent(SmsSent oldSms, DeviceSmsStatus status)
    {
        var sms = _smsCtx.SmsSent.FirstOrDefault(x => x.Id == oldSms.Id);
        if (sms == null) return;
        sms.SmsStatus = status;
        sms.ResponseText = oldSms.ResponseText;
        await _smsCtx.SaveChangesAsync();
    }

    private async Task UpdatePreviousSmsText(ReceivedSms sms)
    {
        var previousSms = _ctx.ReceivedSms
            .Where(x => x.Sender != null && x.Sender.Equals(sms.Sender))
            .Where(x => x.Parsed)
            .Where(x => x.FinishStatus == SmsFinishStatus.NotFinished)
            .LastOrDefault(x => x.SmsStatus == SmsStatus.Fine || x.SmsStatus == SmsStatus.Reminder);
        if (previousSms == null) return;
        previousSms.Text = $"{previousSms.Text?.Trim()} {sms.Text}";
        previousSms.FinishStatus = SmsFinishStatus.Finished;
        await _ctx.SaveChangesAsync();
    }

    private SerialPort? Setup()
    {
        if (!SetComPort())
        {
            ConsoleLog("Port can't be found!", ConsoleColor.Red);
            return null;
        }

        if (string.IsNullOrEmpty(PortName)) return null;
        var serialPort = new SerialPort(PortName, BaudRate);
        var initialized = Init(serialPort);
        if (initialized) return serialPort;
        ConsoleLog("Failed to initialize the device", ConsoleColor.Red);
        return null;
    }

    private bool SetComPort()
    {
        var portName = UsbSerialConverterInfo.GetPortName();
        if (string.IsNullOrEmpty(portName)) return false;
        PortName = portName;
        return true;
    }

    private static bool Init(SerialPort serialPort)
    {
        if (serialPort.IsOpen) return true;
        serialPort.Open();
        ConsoleLog("Port has successfully opened", ConsoleColor.Green);

        if (!ExecuteCommand(serialPort, "AT", 200)) return false;
        ExecuteCommand(serialPort, "ATE0", 100);

        var time = DateTime.Now.ToLocalTime().ToString("yy/MM/dd,HH:mm:sszz");
        ExecuteCommand(serialPort, $"AT+CCLK=\"{time}\"", 100);
        ExecuteCommand(serialPort, "AT+CCLK?", 200);
        ExecuteCommand(serialPort, "AT+CLIP=1", 200);
        ExecuteCommand(serialPort, "AT+CMGF=1", 200);
        ExecuteCommand(serialPort, "AT+CSMP=17,167,0,0", 200);

        ExecuteCommand(serialPort, "AT+CREG=1", 200);

        ConsoleLog("####################################");
        ConsoleLog("Initialization finished successfully", ConsoleColor.Green);
        ConsoleLog("####################################");
        return true;
    }


    private static void ConsoleLog(string message, ConsoleColor color = ConsoleColor.White)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}