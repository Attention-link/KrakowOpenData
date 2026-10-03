# Build from the repository root:  docker build -f src/KrakowOpenData.Web/Dockerfile -t krakow-web .
# The SDK stage runs on the build machine's own platform and cross-compiles for the target one, so building for
# another architecture (e.g. --platform linux/amd64 on an arm64 machine) doesn't run the .NET SDK under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish src/KrakowOpenData.Web/KrakowOpenData.Web.csproj -c Release -a $TARGETARCH -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "KrakowOpenData.Web.dll"]
