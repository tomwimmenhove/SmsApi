using sms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(typeof(IUserBroadcast), new UserBroadcast());

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001);
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "Hello world");

app.Run();
