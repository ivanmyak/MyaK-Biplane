FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["MyaKBiplane.Client/MyaKBiplane.Client.csproj", "MyaKBiplane.Client/"]
COPY ["MyaKBiplane.Game/MyaKBiplane.Game.csproj", "MyaKBiplane.Game/"]
COPY ["MyaKBiplane.Core/MyaKBiplane.Core.csproj", "MyaKBiplane.Core/"]
COPY ["MyaKBiplane.Shared/MyaKBiplane.Shared.csproj", "MyaKBiplane.Shared/"]
RUN dotnet restore "MyaKBiplane.Client/MyaKBiplane.Client.csproj"

COPY . .
RUN dotnet publish "MyaKBiplane.Client/MyaKBiplane.Client.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish

FROM nginx:1.27-alpine AS final
COPY MyaKBiplane.Client/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
