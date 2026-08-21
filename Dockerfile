# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY ["PdfRemediation.Api/PdfRemediation.Api.csproj", "PdfRemediation.Api/"]
RUN dotnet restore "PdfRemediation.Api/PdfRemediation.Api.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/PdfRemediation.Api"
RUN dotnet build "PdfRemediation.Api.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "PdfRemediation.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS final
WORKDIR /app
ENV DOTNET_USE_POLLING_FILE_WATCHER=1
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PdfRemediation.Api.dll"]
