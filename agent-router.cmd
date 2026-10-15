@rem Repo-local development launcher for the Agent Router CLI.
@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%src\AgentRouter.Cli\AgentRouter.Cli.csproj"

dotnet run --project "%PROJECT%" --no-restore -v:q -- %*
exit /b %ERRORLEVEL%