# Build from the repository root:  docker build -f src/KrakowOpenData.Api/Dockerfile -t krakow-api .
# The SDK stage runs on the build machine's own platform and cross-compiles for the target one, so building for
# another architecture (e.g. --platform linux/amd64 on an arm64 machine) doesn't run the .NET SDK under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish src/KrakowOpenData.Api/KrakowOpenData.Api.csproj -c Release -a $TARGETARCH -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# /data holds the safety store and the open-data snapshot cache; mount a volume there to keep them across restarts.
# Created here and owned by the image's non-root "app" user so a fresh named volume inherits that ownership.
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "KrakowOpenData.Api.dll"]
