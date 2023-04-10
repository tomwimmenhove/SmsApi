using sms_daemon;
using Microsoft.Extensions.Configuration;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

Daemon daemon;
try
{
    daemon = new Daemon(configuration);
}
catch (Exception e)
{
    Console.Error.WriteLine($"Failed to initialize daemon: {e.Message}");
    return;
}

await daemon.Run();