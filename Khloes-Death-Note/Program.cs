using Discord.Interactions;
using Discord.WebSocket;
using Khloes_Death_Note.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddYamlFile("_config.yml", false);       // Add the config file to IConfiguration variables
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<DiscordSocketClient>();       // Add the discord client to services
        services.AddSingleton<InteractionService>();        // Add the interaction service to services
        services.AddHostedService<InteractionHandlingService>();    // Add the slash command handler
        services.AddHostedService<DiscordStartupService>();         // Add the discord startup service
        services.AddSingleton<KDNService>();
    })
    .Build();

await host.RunAsync();