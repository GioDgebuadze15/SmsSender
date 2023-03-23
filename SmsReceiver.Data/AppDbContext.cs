using Microsoft.EntityFrameworkCore;
using SmsReceiver.Models;

namespace SmsReceiver.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ReceivedSms> ReceivedSms { get; set; }
}