using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmsReceiver.AppServices.CheckerService;
using SmsReceiver.AppServices.SmsService.Reader;
using SmsReceiver.AppServices.SmsService.Sender;
using SmsReceiver.Data;


const int interval = 6 * 60 * 60 * 1000;


var config = Configure();

if (config == null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Configuration is not successful!");
    Console.ResetColor();
    return;
}

var serviceProvider = ProvideService(config);


var fineWebsiteBaseUrl = config["FineWebsiteBaseUrl"];
var smsSenderApiBaseUrl = config["SmsSenderApi:BaseUrl"];
var smsSenderUsername = config["SmsSenderApi:Username"];
var smsSenderPassword = config["SmsSenderApi:Password"];
var numbersForMyGpsCars = config["SmsSenderApi:NumbersForMyGpsCars"];
var numbersForWissolCars = config["SmsSenderApi:NumbersForWissolCars"];
var geliosApiBaseUrl = config["GeliosApi:BaseUrl"];
var geliosUsername = config["GeliosApi:Username"];
var geliosPassword = config["GeliosApi:Password"];


var smsReader = ActivatorUtilities.CreateInstance<SmsReaderService>(serviceProvider);
var smsSender = ActivatorUtilities.CreateInstance<SmsSenderService>
(
    serviceProvider,
    smsSenderApiBaseUrl!,
    smsSenderUsername!,
    smsSenderPassword!,
    numbersForMyGpsCars!,
    numbersForWissolCars!,
    geliosApiBaseUrl!,
    geliosUsername!,
    geliosPassword!
);
var fineStatusChecker =
    ActivatorUtilities.CreateInstance<FineStatusCheckerService>(serviceProvider, fineWebsiteBaseUrl!);


var timer = new Timer(_ => { Task.Run(async () => await fineStatusChecker.CheckForFineStatus()); }, null,
    TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));


while (true)
{
    await smsReader.SendSmsToDevice();
    await smsReader.Run();
    await smsSender.Run();

    await Task.Delay(1000);
}


// Task.WaitAll(CreateCalls().ToArray());
//
// IEnumerable<Task> CreateCalls()
// {
//     while (true)
//     {
//         yield return RunTasks();
//     }
// }
//
// async Task RunTasks()
// {
//     // while (true)
//     // {
//     try
//     {
//         // await _gate.WaitAsync();
//         var task1 = Task.Run(async () => { await smsRepository.SendSmsToDevice(); });
//         // _gate.Release();
//         await _gate.WaitAsync();
//         var task2 = Task.Run(async () => { await smsRepository.Run(); });
//         // _gate.Release();
//         await _gate.WaitAsync();
//         var task3 = Task.Run(async () => { await smsSender.Run(); });
//         // _gate.Release();
//         task1.Wait();
//         task2.Wait();
//         task3.Wait();
//     }
//     catch (Exception e)
//     {
//         Console.ForegroundColor = ConsoleColor.DarkRed;
//         Console.WriteLine(e);
//         Console.ResetColor();
//         throw;
//     }
//     // }
// }

IConfigurationRoot? Configure()
{
    //Todo: check the environment
    // var isDevelopment = Environment.GetEnvironmentVariable("ENVIRONMENT") ;


    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.production.json",
            optional: false, reloadOnChange: true)
        .Build();

    if (configBuilder["FineWebsiteBaseUrl"] == null ||
        configBuilder["SmsSenderApi:BaseUrl"] == null ||
        configBuilder["SmsSenderApi:Username"] == null ||
        configBuilder["SmsSenderApi:Password"] == null ||
        configBuilder["SmsSenderApi:NumbersForMyGpsCars"] == null ||
        configBuilder["SmsSenderApi:NumbersForWissolCars"] == null ||
        configBuilder["GeliosApi:BaseUrl"] == null ||
        configBuilder["GeliosApi:Username"] == null ||
        configBuilder["GeliosApi:Password"] == null)
    {
        return null;
    }

    return configBuilder;
}

ServiceProvider ProvideService(IConfiguration configuration)
    => new ServiceCollection()
        .AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")))
        .AddDbContext<SmsDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("SmsConnection")))
        .BuildServiceProvider();