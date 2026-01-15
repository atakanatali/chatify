using Chatify.BuildingBlocks.DependencyInjection;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.DependencyInjection;
using Chatify.Api.Hubs;
using Chatify.Api.Middleware;
using Serilog;

namespace Chatify.Api;

public static class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((context, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .ConfigureChatifySerilog(context.Configuration);
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });

    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // BuildingBlocks
            services.AddSingleton<IClockService, SystemClockService>();
            services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

            // Logging
            services.AddChatifyLogging(Configuration);

            // Infrastructure Providers
            services.AddDatabase(Configuration);
            services.AddCaching(Configuration);
            services.AddMessageBroker(Configuration);

            // Application Services
            services.AddChatifyChatApplication();

            // ASP.NET Core Services
            services.AddControllers();
            services.AddSignalR();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<ChatHubService>("/hubs/chat");
            });
        }
    }
}
