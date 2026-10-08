# Repo-local development launcher for the Claude Code Gateway CLI.
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GatewayArgs
)

$project = Join-Path $PSScriptRoot "src/ClaudeCodeGateway.Cli/ClaudeCodeGateway.Cli.csproj"
dotnet run --project $project --no-restore -v:q -- @GatewayArgs
exit $LASTEXITCODE