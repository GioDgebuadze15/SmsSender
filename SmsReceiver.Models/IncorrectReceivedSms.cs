using System.Text;

namespace SmsReceiver.Models;

public class IncorrectReceivedSms
{
    public string? Sender { get; set; }


    public StringBuilder Text { get; set; } = new();

    public List<string> Indexes { get; set; } = new();
    public string? Time { get; set; }
}