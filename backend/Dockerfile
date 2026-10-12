# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Tahona.slnx ./
COPY backend/src/Tahona.Domain/Tahona.Domain.csproj src/Tahona.Domain/
COPY backend/src/Tahona.Application/Tahona.Application.csproj src/Tahona.Application/
COPY backend/src/Tahona.Infrastructure/Tahona.Infrastructure.csproj src/Tahona.Infrastructure/
COPY backend/src/Tahona.Api/Tahona.Api.csproj src/Tahona.Api/
RUN dotnet restore src/Tahona.Api/Tahona.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Tahona.Api/Tahona.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# ICU + tzdata are required for Asia/Amman time zone handling and Arabic culture formatting.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata libicu-dev && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    Storage__Root=/data/storage
VOLUME ["/data/storage"]
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Tahona.Api.dll"]
