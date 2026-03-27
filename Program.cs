using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Models;
using PeopleCounter_Backend.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://0.0.0.0:5000");
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "PeopleCounter.Auth";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSignalR", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true)
            .AllowCredentials();
    });
});


builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.AddSingleton<SensorCacheService>();
builder.Services.AddSingleton<MqttMessageProcessor>();
builder.Services.AddSingleton<MqttService>();
builder.Services.AddHostedService<MqttBackgroundService>();

builder.Services.AddScoped<PeopleCounterRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<SensorRepository>();
builder.Services.AddScoped<SensorHealthService>();

//builder.Services.AddSingleton<DataRetentionService>();
//builder.Services.AddHostedService<DataRetentionBackgroundService>();
builder.Services.AddHostedService<SensorHealthBackgroundService>();

builder.Services.AddMemoryCache();

builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sqlserver",
        timeout: TimeSpan.FromSeconds(5));

builder.Host.UseWindowsService();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(err => err.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
}));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.UseCors("AllowSignalR");
app.UseWebSockets();
app.MapHub<PeopleCounterHub>("/peopleCounterHub");
app.MapHealthChecks("/health");

try
{
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Host terminated unexpectedly");
    throw;
}