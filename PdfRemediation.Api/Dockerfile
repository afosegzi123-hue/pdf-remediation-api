FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Force Workstation GC and disable shared compilation to prevent OOM segfaults on Render Free Tier (512MB limit)
ENV DOTNET_gcServer=0
ENV DOTNET_EnableDiagnostics=0

COPY ["PdfRemediation.Api/PdfRemediation.Api.csproj", "PdfRemediation.Api/"]
RUN dotnet restore "PdfRemediation.Api/PdfRemediation.Api.csproj"
COPY . .
WORKDIR /src/PdfRemediation.Api
RUN dotnet publish "PdfRemediation.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV DOTNET_gcServer=0
ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PdfRemediation.Api.dll"]
