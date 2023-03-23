namespace SmsReceiver.AppServices.SmsService.Reader;

public interface ISmsReaderService
{
    Task Run();
    Task SendSmsToDevice();
}