FROM debian:bookworm-slim AS furnace-build
ARG FURNACE_REF=fa0859f800e98cde73ea072f685e7335d4bdcc81
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates cmake g++ git libgl-dev make pkg-config \
    && rm -rf /var/lib/apt/lists/*
RUN git clone --recursive https://github.com/tildearrow/furnace.git /furnace \
    && cd /furnace \
    && git checkout "$FURNACE_REF" \
    && git submodule update --init --recursive
RUN cmake -S /furnace -B /furnace/build \
      -DBUILD_GUI=OFF -DUSE_SDL2=OFF -DUSE_RTMIDI=OFF -DWITH_LOCALE=OFF \
      -DWITH_DEMOS=OFF -DWITH_INSTRUMENTS=OFF -DWITH_WAVETABLES=OFF \
      -DWITH_JACK=OFF -DWITH_PORTAUDIO=OFF -DUSE_BACKWARD=OFF \
      -DCMAKE_CXX_FLAGS="-include climits -include cstring" \
    && cmake --build /furnace/build --parallel 2
COPY native/chiptune-renderer/main.cpp /furnace/src/main.cpp
RUN touch /furnace/src/main.cpp \
    && cmake --build /furnace/build --parallel 2 \
    && cp /furnace/build/furnace /homelynx-chiptune-renderer

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ ./src/
RUN dotnet restore src/TorrentBot2.sln
RUN dotnet publish src/TorrentBot.Adapters.Telegram.Host/TorrentBot.Adapters.Telegram.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ARG TARGETARCH=amd64
ARG DENO_VERSION=2.9.4
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg chromium ca-certificates curl unzip \
    && case "$TARGETARCH" in \
         amd64) YTDLP_ASSET=yt-dlp_linux ;; \
         arm64) YTDLP_ASSET=yt-dlp_linux_aarch64 ;; \
         *) echo "Unsupported architecture: $TARGETARCH" && exit 1 ;; \
       esac \
    && case "$TARGETARCH" in \
         amd64) DENO_ASSET=deno-x86_64-unknown-linux-gnu.zip ;; \
         arm64) DENO_ASSET=deno-aarch64-unknown-linux-gnu.zip ;; \
         *) echo "Unsupported architecture: $TARGETARCH" && exit 1 ;; \
       esac \
    && curl -fsSL "https://github.com/yt-dlp/yt-dlp-master-builds/releases/download/2026.08.19.064229/${YTDLP_ASSET}" -o /usr/local/bin/yt-dlp \
    && curl -fsSL "https://github.com/denoland/deno/releases/download/v${DENO_VERSION}/${DENO_ASSET}" -o /tmp/deno.zip \
    && unzip -q /tmp/deno.zip -d /usr/local/bin \
    && chmod +x /usr/local/bin/deno /usr/local/bin/yt-dlp \
    && apt-get purge -y --auto-remove curl \
    && rm -rf /var/lib/apt/lists/* /tmp/deno.zip
COPY --from=build /app/publish .
COPY --from=furnace-build /homelynx-chiptune-renderer /usr/local/bin/homelynx-chiptune-renderer
COPY --from=furnace-build /furnace/LICENSE.GPLv2 /usr/share/doc/homelynx-chiptune-renderer/LICENSE.GPLv2
COPY --from=furnace-build /furnace/LICENSE.GPLv3 /usr/share/doc/homelynx-chiptune-renderer/LICENSE.GPLv3
COPY THIRD_PARTY_NOTICES.md /usr/share/doc/homelynx-chiptune-renderer/THIRD_PARTY_NOTICES.md
ENV TORRENTBOT_ENABLE_NEW_ENGINE=true
ENV TORRENTBOT_ENABLE_LEGACY_PYTHON=false
ENV YTDLP_PATH=/usr/local/bin/yt-dlp
ENV FFMPEG_PATH=/usr/bin/ffmpeg
ENV PLAYWRIGHT_EXECUTABLE_PATH=/usr/bin/chromium
ENV CHIPTUNE_RENDERER_PATH=/usr/local/bin/homelynx-chiptune-renderer
LABEL org.homelynx.component="bot"
ENTRYPOINT ["dotnet", "TorrentBot.Adapters.Telegram.Host.dll"]
