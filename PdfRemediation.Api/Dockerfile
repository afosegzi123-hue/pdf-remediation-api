FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PdfRemediation.Api/PdfRemediation.Api.csproj", "PdfRemediation.Api/"]
RUN dotnet restore "PdfRemediation.Api/PdfRemediation.Api.csproj"
COPY . .
WORKDIR /src/PdfRemediation.Api
RUN dotnet publish "PdfRemediation.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM ubuntu:22.04 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    ca-certificates \
    libicu-dev \
    && rm -rf /var/lib/apt/lists/*

RUN wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends aspnetcore-runtime-8.0 \
    && rm -rf /var/lib/apt/lists/* \
    && rm packages-microsoft-prod.deb

ENV DOTNET_gcServer=0
ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PdfRemediation.Api.dll"]
