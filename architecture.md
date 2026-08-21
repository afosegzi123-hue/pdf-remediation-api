# System Architecture
[Vercel Frontend]
│
▼ (HTTPS POST multipart/form-data)
[Render .NET 8 Web API Container]
├──► [PostgreSQL Managed Database (Npgsql EF Core)]
└──► [In-Memory ZIP Stream Pipeline Engine]
## Component Technical Specs
* **API Framework:** ASP.NET Core 8 Web API (`Microsoft.AspNetCore.Mvc`).
* **ORM:** Entity Framework Core 8 (`Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`).
* **Container Environment:** Linux Debian-slim base runtime (`mcr.microsoft.com/dotnet/aspnet:8.0`).