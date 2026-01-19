# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY src/StockAnalyzer.Api/StockAnalyzer.Api.csproj src/StockAnalyzer.Api/
COPY src/StockAnalyzer.Core/StockAnalyzer.Core.csproj src/StockAnalyzer.Core/
RUN dotnet restore src/StockAnalyzer.Api/StockAnalyzer.Api.csproj

# Copy everything else and build
COPY . .
RUN dotnet publish src/StockAnalyzer.Api/StockAnalyzer.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose port
EXPOSE 5000

# Set environment
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "StockAnalyzer.Api.dll"]
