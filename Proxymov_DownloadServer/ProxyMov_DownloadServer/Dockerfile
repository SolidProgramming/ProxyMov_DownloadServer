# Unter https://aka.ms/customizecontainer erfahren Sie, wie Sie Ihren Debugcontainer anpassen und wie Visual Studio dieses Dockerfile verwendet, um Ihre Images für ein schnelleres Debuggen zu erstellen.

# Diese Stufe wird verwendet, wenn sie von VS im Schnellmodus ausgeführt wird (Standardeinstellung für Debugkonfiguration).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

RUN apt-get update \
    && apt-get install -y \
       wget \
       unzip \
       fontconfig \
       locales \
       libatk1.0-0 \
       libc6 \
       libcairo2 \
       libcups2 \
       libdbus-1-3 \
       libexpat1 \
       libfontconfig1 \
       libgcc1 \
       libgdk-pixbuf2.0-0 \
       libglib2.0-0 \
       libgtk-3-0 \
       libnspr4 \
       libpango-1.0-0 \
       libpangocairo-1.0-0 \
       libstdc++6 \
       libx11-6 \
       libx11-xcb1 \
       libxcb1 \
       libxcomposite1 \
       libxcursor1 \
       libxdamage1 \
       libxext6 \
       libxfixes3 \
       libxi6 \
       libxrandr2 \
       libxrender1 \
       libxss1 \
       libxtst6 \
       ca-certificates \
       fonts-liberation \
       libnss3 \
       lsb-release \
       xdg-utils \
       libasound2t64 \
       libdrm2 \
       libgbm1 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /data/downloads \
    && chmod -R 777 /data/downloads \
    && mkdir -p /app/appdata \
    && chmod -R 777 /app/appdata

WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8484

# Diese Stufe wird zum Erstellen des Dienstprojekts verwendet.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ProxyMov_DownloadServer/ProxyMov_DownloadServer.csproj", "ProxyMov_DownloadServer/"]
#COPY ["ProxyMov_DownloadServer.ServiceDefaults/ProxyMov_DownloadServer.ServiceDefaults.csproj", "ProxyMov_DownloadServer.ServiceDefaults/"]
RUN dotnet restore "./ProxyMov_DownloadServer/ProxyMov_DownloadServer.csproj"
COPY . .
WORKDIR "/src/ProxyMov_DownloadServer"
RUN dotnet build "./ProxyMov_DownloadServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Diese Stufe wird verwendet, um das Dienstprojekt zu veröffentlichen, das in die letzte Phase kopiert werden soll.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ProxyMov_DownloadServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Diese Stufe wird in der Produktion oder bei Ausführung von VS im regulären Modus verwendet (Standard, wenn die Debugkonfiguration nicht verwendet wird).
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN printf '#!/bin/sh\numask 0000\nexec dotnet ProxyMov_DownloadServer.dll "$@"\n' > /app/entrypoint.sh \
    && chmod +x /app/entrypoint.sh

USER 1000:100
ENTRYPOINT ["/app/entrypoint.sh"]