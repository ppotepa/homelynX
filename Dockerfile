FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ ./src/
RUN dotnet restore src/TorrentBot2.sln
RUN dotnet publish src/TorrentBot.Adapters.Telegram.Host/TorrentBot.Adapters.Telegram.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ARG TARGETARCH=amd64
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg chromium ca-certificates curl \
    && case "$TARGETARCH" in \
         amd64) YTDLP_ASSET=yt-dlp_linux ;; \
         arm64) YTDLP_ASSET=yt-dlp_linux_aarch64 ;; \
         *) echo "Unsupported architecture: $TARGETARCH" && exit 1 ;; \
       esac \
    && curl -fsSL "https://github.com/yt-dlp/yt-dlp-master-builds/releases/download/2026.08.19.064229/${YTDLP_ASSET}" -o /usr/local/bin/yt-dlp \
    && chmod +x /usr/local/bin/yt-dlp \
    && apt-get purge -y --auto-remove curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV TORRENTBOT_ENABLE_NEW_ENGINE=true
ENV TORRENTBOT_ENABLE_LEGACY_PYTHON=false
ENV YTDLP_PATH=/usr/local/bin/yt-dlp
ENV FFMPEG_PATH=/usr/bin/ffmpeg
ENV PLAYWRIGHT_EXECUTABLE_PATH=/usr/bin/chromium
LABEL org.homelynx.component="bot"
ENTRYPOINT ["dotnet", "TorrentBot.Adapters.Telegram.Host.dll"]
