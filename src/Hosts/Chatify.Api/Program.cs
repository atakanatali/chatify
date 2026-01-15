using Chatify.BuildingBlocks.DependencyInjection;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.DependencyInjection;
using Chatify.Api.Hubs;
using Chatify.Api.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ConfigureChatifySerilog(context.Configuration);
});

var configuration = builder.Configuration;

// BuildingBlocks
builder.Services.AddSingleton<IClockService, SystemClockService>();
builder.Services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

// Logging
builder.Services.AddChatifyLogging(configuration);

// Infrastructure Providers
builder.Services.AddDatabase(configuration);
builder.Services.AddCaching(configuration);
builder.Services.AddMessageBroker(configuration);

// Application Services
builder.Services.AddChatifyChatApplication();

// ASP.NET Core Services
builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRouting();

// Eğer authentication kullanıyorsan aç:
// app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHubService>("/hubs/chat");

app.Run();
