
# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy project file and restore dependencies
COPY Restaurant.Api.csproj ./
RUN dotnet restore Restaurant.Api.csproj

# Copy source code
COPY . .

# Publish the application
RUN dotnet publish Restaurant.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 2: Run the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "Restaurant.Api.dll"]
