@rem Repo-local development launcher for the Claude Code Gateway CLI.
@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%src\ClaudeCodeGateway.Cli\ClaudeCodeGateway.Cli.csproj"

dotnet run --project "%PROJECT%" --no-restore -v:q -- %*
exit /b %ERRORLEVEL%