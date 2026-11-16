using MyaKBiplane.Server.Hubs;
using MyaKBiplane.Server.Logging;
using MyaKBiplane.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
LoggingProviderFactory.Configure(builder.Logging, builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            policy.SetIsOriginAllowed(_ => true);
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

app.MapGet("/", () => Results.Ok(new
{
    name = GameInfo.Title,
    version = GameInfo.Version,
    protocolVersion = GameInfo.ProtocolVersion,
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<GameHub>("/hubs/game");

app.Logger.LogInformation(
    "Сервер {Title} {Version} запущен. Протокол: {ProtocolVersion}",
    GameInfo.Title,
    GameInfo.Version,
    GameInfo.ProtocolVersion);

app.Run();
