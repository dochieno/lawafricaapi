# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY LawAfrica.API.csproj ./
RUN dotnet restore "./LawAfrica.API.csproj"

# Copy everything else and publish
COPY . ./
RUN dotnet publish "./LawAfrica.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Render sets PORT. ASP.NET must listen on it.
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT}

COPY --from=build /app/publish ./

# Your assembly name from your build output
ENTRYPOINT ["dotnet", "LawAfrica.API.dll"]
