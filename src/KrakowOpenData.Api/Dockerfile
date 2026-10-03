# Build from the repository root:  docker build -f src/KrakowOpenData.Api/Dockerfile -t krakow-api .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/KrakowOpenData.Api/KrakowOpenData.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "KrakowOpenData.Api.dll"]
