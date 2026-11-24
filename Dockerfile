# Dashboard (Angular) build: output src/ChatServe.Api/wwwroot mein jata hai
FROM node:22-alpine AS dashboard
WORKDIR /repo/dashboard
COPY dashboard/package.json dashboard/package-lock.json ./
RUN npm ci
COPY dashboard/ ./
RUN npx ng build

# API build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ChatServe.Api/ChatServe.Api.csproj src/ChatServe.Api/
RUN dotnet restore src/ChatServe.Api/ChatServe.Api.csproj
COPY src/ src/
COPY --from=dashboard /repo/src/ChatServe.Api/wwwroot src/ChatServe.Api/wwwroot
RUN dotnet publish src/ChatServe.Api/ChatServe.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ChatServe.Api.dll"]
