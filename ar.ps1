param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GatewayArgs
)

& (Join-Path $PSScriptRoot "agent-router.ps1") @GatewayArgs
exit $LASTEXITCODE