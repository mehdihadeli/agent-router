# CLI Reference

The gateway is available as a repo-local launcher and as a packaged .NET tool.

The interactive initialization flow generates the gateway configuration files and required environment variables. The generated local proxy listens on `127.0.0.1` and uses a generated authentication token.

## Commands

`serve`

- Aliases: `s`, `run`
- Starts the gateway host.

## Options

`--url`, `-u`

- Absolute HTTP or HTTPS listener URL.

`--port`, `-p`

- Listener port. Expanded to `http://0.0.0.0:<port>`.

## Examples

```bash
./claude-code-gateway serve --port 4600
./claude-code-gateway s -u http://127.0.0.1:4600
claude-code-gateway --help
```
