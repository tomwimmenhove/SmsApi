using sms.Controllers;
using sms.Auth;
using Microsoft.OpenApi.Models;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(typeof(IBroadcaster), new Broadcaster());
builder.Services.Configure<SmsControllerSettings>(configuration.GetSection("SmsController"));
builder.Services.Configure<ConnectionValidationSettings>(configuration.GetSection("Auth"));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Configure(configuration.GetSection("Kestrel"));
});

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SMS API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<ConnectionValidation>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
