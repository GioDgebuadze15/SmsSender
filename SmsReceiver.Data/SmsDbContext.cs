using Microsoft.EntityFrameworkCore;
using SmsReceiver.Models;

namespace SmsReceiver.Data;

public class SmsDbContext : DbContext
{
    public SmsDbContext(DbContextOptions<SmsDbContext> options) : base(options)
    {
    }
    
    public DbSet<SmsSent> SmsSent { get; set; }
}