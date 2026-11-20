using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace MyaKBiplane.Server.Logging;

/// <summary>
/// Настраивает Serilog по секциям конфигурации <c>LoggingProviders</c> и <c>LogFiles</c>.
/// </summary>
/// <remarks>
/// Схема повторяет подход проекта SchoolShedule: консоль, обычный файл и JSON-файл
/// включаются независимо друг от друга через конфигурацию.
/// </remarks>
public static class LoggingProviderFactory
{
    /// <summary>
    /// Применяет настройки Serilog к коллекции провайдеров логирования.
    /// </summary>
    /// <param name="logging">Коллекция провайдеров логирования.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    public static void Configure(ILoggingBuilder logging, IConfiguration configuration)
    {
        logging.ClearProviders();

        var providers = configuration.GetSection("LoggingProviders");
        var files = configuration.GetSection("LogFiles");

        var rootPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            files["Root"] ?? Path.Combine("Data", "Logs"),
            DateTime.Now.Year.ToString(CultureInfo.InvariantCulture));

        if (!Directory.Exists(rootPath))
        {
            Directory.CreateDirectory(rootPath);
        }

        var retention = int.TryParse(files["RetentionDays"], out var parsedRetention)
            ? parsedRetention
            : 30;

        var levels = configuration.GetSection("Logging:LogLevel");
        var minLevel = levels["Default"] ?? levels["MyaKBiplane"] ?? "Information";

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLevel(minLevel));

        // Категорийные уровни из Logging:LogLevel переносятся в Serilog как переопределения,
        // иначе шумные категории вроде SignalR игнорируют настройки приложения.
        foreach (var category in levels.GetChildren())
        {
            if (string.Equals(category.Key, "Default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(category.Value))
            {
                loggerConfiguration.MinimumLevel.Override(category.Key, ParseLevel(category.Value));
            }
        }

        var providerActions = new Dictionary<string, Action>
        {
            ["Console"] = () => ConfigureConsole(loggerConfiguration),
            ["File"] = () => ConfigureFile(loggerConfiguration, rootPath, retention),
            ["Monitoring"] = () => ConfigureMonitoringFile(loggerConfiguration, rootPath, retention)
        };

        foreach (var provider in providerActions)
        {
            if (providers.GetValue<bool>(provider.Key))
            {
                provider.Value();
            }
        }

        Log.Logger = loggerConfiguration.CreateLogger();
        logging.AddSerilog(Log.Logger);
    }

    private static void ConfigureConsole(LoggerConfiguration logging)
    {
        logging.WriteTo.Console(
            formatProvider: CultureInfo.InvariantCulture,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    }

    private static void ConfigureFile(LoggerConfiguration logging, string rootPath, int retention)
    {
        var fileName = $"{DateTime.Now:ddMMyyyy}.log";
        logging.WriteTo.File(
            path: Path.Combine(rootPath, fileName),
            formatProvider: CultureInfo.InvariantCulture,
            rollingInterval: RollingInterval.Infinite,
            retainedFileCountLimit: retention);
    }

    private static void ConfigureMonitoringFile(LoggerConfiguration logging, string rootPath, int retention)
    {
        var fileName = $"{DateTime.Now:ddMMyyyy}.json";
        logging.WriteTo.File(
            formatter: new JsonFormatter(),
            path: Path.Combine(rootPath, fileName),
            rollingInterval: RollingInterval.Infinite,
            retainedFileCountLimit: retention);
    }

    private static LogEventLevel ParseLevel(string level)
    {
        return Enum.TryParse<LogEventLevel>(level, true, out var parsed)
            ? parsed
            : LogEventLevel.Information;
    }
}
