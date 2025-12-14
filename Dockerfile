# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Kopiuj plik projektu i przywróć zależności
COPY *.csproj ./
RUN dotnet restore

# Kopiuj resztę kodu i zbuduj
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Kopiuj zbudowaną aplikację
COPY --from=build /app/publish .

# Render.com używa zmiennej PORT
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose port
EXPOSE 8080

# Start aplikacji
ENTRYPOINT ["dotnet", "KSeF.Backend.dll"]