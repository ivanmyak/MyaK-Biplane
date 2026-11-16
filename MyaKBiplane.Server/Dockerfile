FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["MyaKBiplane.Server/MyaKBiplane.Server.csproj", "MyaKBiplane.Server/"]
COPY ["MyaKBiplane.Core/MyaKBiplane.Core.csproj", "MyaKBiplane.Core/"]
COPY ["MyaKBiplane.Shared/MyaKBiplane.Shared.csproj", "MyaKBiplane.Shared/"]
RUN dotnet restore "MyaKBiplane.Server/MyaKBiplane.Server.csproj"

COPY . .
WORKDIR "/src/MyaKBiplane.Server"
RUN dotnet build "MyaKBiplane.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MyaKBiplane.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyaKBiplane.Server.dll"]
