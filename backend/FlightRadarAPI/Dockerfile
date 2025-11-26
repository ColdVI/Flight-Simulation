# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY backend/FlightRadarAPI/FlightRadarAPI.csproj backend/FlightRadarAPI/
RUN dotnet restore backend/FlightRadarAPI/FlightRadarAPI.csproj

COPY backend/FlightRadarAPI/ backend/FlightRadarAPI/
COPY frontend/ frontend/

RUN dotnet publish backend/FlightRadarAPI/FlightRadarAPI.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY --from=build /src/frontend ./frontend

ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001

ENTRYPOINT ["dotnet", "FlightRadarAPI.dll"]
