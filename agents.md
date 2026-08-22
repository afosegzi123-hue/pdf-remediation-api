# Agent Roles & Orchestration

## 1. Solution Architect Agent (`@architect`)
* **Responsibility:** Validates the cross-platform separation of concerns between Vercel, Render, and PostgreSQL. Ensures connection resilience, secure environment configuration, and correct multipart streaming headers.

## 2. Backend C# .NET Engineer (`@dotnet-engineer`)
* **Responsibility:** Implements the ASP.NET Core 8 Web API controllers, Entity Framework Core models, database context setup, exception handling middleware, and multipart file stream routines.

## 3. PDF Heuristics Engineer (`@pdf-remediator`)
* **Responsibility:** Implements manipulation logic using `iText7` to handle document object model parsing, structure tree writing, 1:1 absolute pagination, true alignment geometrical analysis, and fixed-height bounding boxes to guarantee exact visual replicas.

## 4. DevOps & Deployment Agent (`@devops`)
* **Responsibility:** Manages the Render multi-stage Docker build files (`render.yaml`), Vercel deployments, environment variables, health-check configurations, and container startup sequences.