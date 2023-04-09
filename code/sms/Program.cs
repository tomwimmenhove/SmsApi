using sms;
using Microsoft.OpenApi.Models;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(typeof(IUserBroadcast), new UserBroadcast());
builder.Services.AddSingleton(configuration);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001);
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

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
