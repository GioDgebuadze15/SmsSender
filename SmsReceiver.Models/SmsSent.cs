namespace SmsReceiver.Models
{
    public class SmsSent
    {
        public int Id { get; set; }
        
        public string Imei { get; set; }
        
        public string Name { get; set; }
        
        public string MobileNumber { get; set; }
        
        public int? SerialNumber { get; set; }
        
        public string Command { get; set; }
        
        public string? ResponseText { get; set; }

        public DeviceSmsStatus SmsStatus { get; set; } = DeviceSmsStatus.NotSent;
        
        public DateTime Created { get; set; } = DateTime.Now;
    }
}