# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Kopiuj TYLKO plik projektu (nie solution!)
COPY *.csproj ./
RUN dotnet restore

# Kopiuj resztę kodu i zbuduj
COPY . ./

# Buduj konkretny projekt, nie solution
RUN dotnet publish *.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Kopiuj zbudowaną aplikację
COPY --from=build /app/publish .

# Render.com używa zmiennej PORT
ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
ENV ASPNETCORE_ENVIRONMENT=Production

# Start aplikacji - POPRAWIONA NAZWA!
ENTRYPOINT ["dotnet", "KSeF_Backend.dll"]