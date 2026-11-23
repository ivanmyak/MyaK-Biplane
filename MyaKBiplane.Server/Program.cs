using System.Net.WebSockets;
using MyaKBiplane.Server.Hubs;
using MyaKBiplane.Server.Logging;
using MyaKBiplane.Server.Rooms;
using MyaKBiplane.Shared;

var builder = WebApplication.CreateBuilder(args);

// Хостинги вроде Render передают порт через PORT; в Docker без этой переменной
// остаётся ASPNETCORE_URLS=http://+:8080 из Dockerfile.
var port = Environment.GetEnvironmentVariable("PORT");

if (int.TryParse(port, out var httpPort) && httpPort is > 0 and <= 65535)
{
    builder.WebHost.UseUrls($"http://+:{httpPort}");
}

builder.Configuration.AddEnvironmentVariables();
LoggingProviderFactory.Configure(builder.Logging, builder.Configuration);

// Версия задаётся при деплое через App__Version (см. scripts/deploy-fly.ps1)
// и совпадает с версией клиента из appsettings.json на GitHub Pages.
var appVersion = builder.Configuration["App:Version"] ?? GameInfo.Version;

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 64 * 1024;
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<RoomManager>();
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
    version = appVersion,
    protocolVersion = GameInfo.ProtocolVersion,
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<GameHub>("/hubs/game");

// Минимальный WebSocket-echo для диагностики чистого сетевого RTT.
// Клиент шлёт метку времени, сервер возвращает её как есть: задержка
// получается без SignalR, игровой симуляции и .NET<->JS-маршаллинга.
app.Map("/ws/ping", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var buffer = new byte[128];

    while (socket.State == WebSocketState.Open)
    {
        var result = await socket.ReceiveAsync(
            new ArraySegment<byte>(buffer), context.RequestAborted);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
            break;
        }

        await socket.SendAsync(
            new ArraySegment<byte>(buffer, 0, result.Count),
            result.MessageType,
            result.EndOfMessage,
            context.RequestAborted);
    }
});

app.Logger.LogInformation(
    "Сервер {Title} {Version} запущен. Протокол: {ProtocolVersion}",
    GameInfo.Title,
    appVersion,
    GameInfo.ProtocolVersion);

app.Run();
