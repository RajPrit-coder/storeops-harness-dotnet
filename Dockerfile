# StoreOps API container (spec §3.4 deployment: local Docker).
# Build:  docker build -t storeops-api .      Run: docker compose up
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/StoreOps.Api/StoreOps.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StoreOps.Api.dll"]
