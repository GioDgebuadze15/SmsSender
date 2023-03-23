namespace SmsReceiver.Models;

public class ResponseStatus
{
    public bool success { get; set; }
    public ErrorDetails error { get; set; }
}