# syntax=docker/dockerfile:1
# Root Dockerfile for Render (repo root = build context)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/Bagly.Api/Bagly.Api.csproj backend/Bagly.Api/
RUN dotnet restore backend/Bagly.Api/Bagly.Api.csproj

COPY backend/Bagly.Api/ backend/Bagly.Api/
RUN dotnet publish backend/Bagly.Api/Bagly.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /app/publish .

CMD ["sh", "-c", "dotnet Bagly.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
