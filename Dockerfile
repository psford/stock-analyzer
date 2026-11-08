# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY src/StockAnalyzer.Api/StockAnalyzer.Api.csproj src/StockAnalyzer.Api/
COPY src/StockAnalyzer.Core/StockAnalyzer.Core.csproj src/StockAnalyzer.Core/
RUN dotnet restore src/StockAnalyzer.Api/StockAnalyzer.Api.csproj

# Copy everything else and build. ImageSharp 3.2+ fails Release builds without a
# Six Labors licence, so pass it as a build secret:
#   docker build --secret id=sixlabors_license,env=SIXLABORS_LICENSE_KEY .
# It is mounted as sixlabors.lic beside the Core project for this step only and
# never lands in an image layer (.dockerignore keeps any local copy out too).
COPY . .
RUN --mount=type=secret,id=sixlabors_license,target=/src/src/StockAnalyzer.Core/sixlabors.lic \
    dotnet publish src/StockAnalyzer.Api/StockAnalyzer.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# eMudhra intermediate CA keys turned out to be recoverable (2026-10-20,
# https://emudhra.we-have-a-problem.com/), and .NET checks outbound TLS against
# this store. Images built before the distros shipped the fix carry the old
# ca-certificates, so upgrade it here; on a current image this is a no-op.
# (aspnet:10.0 is Ubuntu 24.04; aspnet:8.0 was Debian 12.)
RUN apt-get update \
 && apt-get install -y --no-install-recommends --only-upgrade ca-certificates \
 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

# Expose port
EXPOSE 5000

# Set environment
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "StockAnalyzer.Api.dll"]
