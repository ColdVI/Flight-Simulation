# Build stage
# Frontend assets stage
FROM node:18-alpine AS frontend
WORKDIR /app

COPY frontend/ ./
RUN npm install
RUN npm run prepare

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/FlightRadarAPI/FlightRadarAPI.csproj backend/FlightRadarAPI/
RUN dotnet restore backend/FlightRadarAPI/FlightRadarAPI.csproj

COPY backend/FlightRadarAPI/ backend/FlightRadarAPI/
COPY frontend/ frontend/
COPY --from=frontend /app/public ./frontend/public

RUN dotnet publish backend/FlightRadarAPI/FlightRadarAPI.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY --from=build /src/frontend ./frontend

ENV ASPNETCORE_URLS=http://+:5001
EXPOSE 5001

ENTRYPOINT ["dotnet", "FlightRadarAPI.dll"]
