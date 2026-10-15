FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /src
COPY Quantara.sln ./
COPY Directory.*.props ./
COPY Directory.*.targets ./
COPY .config ./.config
COPY services ./services
COPY tools ./tools
RUN dotnet restore services/portfolio/Portfolio.Api/Portfolio.Api.csproj && \
    dotnet restore services/market-data/MarketData.Api/MarketData.Api.csproj

FROM sdk AS publish-portfolio
RUN dotnet publish services/portfolio/Portfolio.Api/Portfolio.Api.csproj \
    -c Release --no-restore -p:QuantaraAutoFormat=false -o /out /p:UseAppHost=false

FROM sdk AS publish-market-data
RUN dotnet publish services/market-data/MarketData.Api/MarketData.Api.csproj \
    -c Release --no-restore -p:QuantaraAutoFormat=false -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS portfolio
ARG APP_UID=1654
RUN apt-get update && apt-get install --no-install-recommends -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish-portfolio /out ./
ENV ASPNETCORE_URLS=http://+:5050
USER ${APP_UID}
EXPOSE 5050
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD curl --fail --silent --show-error --max-time 2 http://127.0.0.1:5050/health || exit 1
ENTRYPOINT ["dotnet", "/app/Portfolio.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS market-data
ARG APP_UID=1654
RUN apt-get update && apt-get install --no-install-recommends -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish-market-data /out ./
ENV ASPNETCORE_URLS=http://+:5051
USER ${APP_UID}
EXPOSE 5051
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD curl --fail --silent --show-error --max-time 2 http://127.0.0.1:5051/health || exit 1
ENTRYPOINT ["dotnet", "/app/MarketData.Api.dll"]
