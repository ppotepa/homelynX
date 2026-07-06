FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ ./src/
RUN dotnet restore src/TorrentBot2.sln
RUN dotnet publish src/TorrentBot.Adapters.Telegram.Host/TorrentBot.Adapters.Telegram.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV TORRENTBOT_ENABLE_NEW_ENGINE=true
ENV TORRENTBOT_ENABLE_LEGACY_PYTHON=false
LABEL org.homelynx.component="bot"
ENTRYPOINT ["dotnet", "TorrentBot.Adapters.Telegram.Host.dll"]