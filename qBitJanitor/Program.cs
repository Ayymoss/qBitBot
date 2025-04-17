﻿using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using qBitJanitor.Api;
using qBitJanitor.Services;
using qBitJanitor.Utilities;
using Refit;
using Serilog;
using Serilog.Events;

namespace qBitJanitor;

public static class Program
{
    public static async Task Main()
    {
        var builder = Host.CreateDefaultBuilder();

        builder.UseSerilog();
        builder.ConfigureHostConfiguration(RegisterHostConfiguration);
        builder.ConfigureServices(RegisterDependencies);

        var host = builder.Build();
        await host.RunAsync();
        await Log.CloseAndFlushAsync();
    }

    private static void RegisterDependencies(HostBuilderContext builder, IServiceCollection serviceCollection)
    {
        serviceCollection.Configure<Configuration>(builder.Configuration);

        var configuration = builder.Configuration.Get<Configuration>() ?? new Configuration();
        RegisterLogging(configuration);

        serviceCollection.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.All }));
        serviceCollection.AddSingleton<GeminiService>();
        serviceCollection.AddSingleton<DiscordBotService>();
        serviceCollection.AddSingleton<MessageProcessingService>();

        serviceCollection.AddSingleton(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, });
        serviceCollection.AddHttpClient("GeminiClient", client =>
            {
                client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta");
                client.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddTypedClient((httpClient, serviceProvider) => RestService.For<IGeminiApi>(httpClient, new RefitSettings
            {
                ContentSerializer = new SystemTextJsonContentSerializer(
                    serviceProvider.GetRequiredService<JsonSerializerOptions>()
                )
            }));

        serviceCollection.AddHostedService<AppEntry>();
    }

    private static void RegisterHostConfiguration(IConfigurationBuilder configurationBuilder)
    {
        if (!Directory.Exists(Path.Join(AppContext.BaseDirectory, "_Configuration")))
            Directory.CreateDirectory(Path.Join(AppContext.BaseDirectory, "_Configuration"));

        configurationBuilder.SetBasePath(Path.Join(AppContext.BaseDirectory, "_Configuration"))
            .AddJsonFile("Configuration.json", false, true);
    }

    private static void RegisterLogging(Configuration configuration)
    {
        if (!Directory.Exists(Path.Join(AppContext.BaseDirectory, "_Log")))
            Directory.CreateDirectory(Path.Join(AppContext.BaseDirectory, "_Log"));

        var loggerConfig = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Join(AppContext.BaseDirectory, "_Log", "qBitBot-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ShortSourceContext}] {Message:lj}{NewLine}{Exception}");

        if (configuration.Debug)
        {
            loggerConfig.MinimumLevel.Information();
            loggerConfig.MinimumLevel.Override("qBitBot", LogEventLevel.Debug);
        }
        else
        {
            loggerConfig.MinimumLevel.Warning();
            loggerConfig.MinimumLevel.Override("qBitBot", LogEventLevel.Information);
        }

        Log.Logger = loggerConfig.CreateLogger();
    }
}
