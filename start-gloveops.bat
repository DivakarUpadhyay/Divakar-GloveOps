@echo off
REM Divakar GloveOps - build and start on http://localhost:5080
cd /d "%~dp0"
dotnet build src\Divakar.GloveOps.Web -c Debug -v q || exit /b 1
start "" http://localhost:5080
cd src\Divakar.GloveOps.Web
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5080
dotnet bin\Debug\net10.0\Divakar.GloveOps.Web.dll
