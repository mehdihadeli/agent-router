namespace AgentRouter.Storage.Sqlite;

/// <summary>
/// Ordered schema migrations. The applied version is tracked in SQLite's <c>user_version</c>
/// pragma, so upgrades are a simple "apply everything above the current version" loop.
/// </summary>
internal static class DatabaseSchema
{
    internal const int CurrentVersion = 1;

    /// <summary>Migration scripts ordered by target version, starting at version 1.</summary>
    internal static IReadOnlyList<string> Migrations { get; } = new[] { Version1 };

    private const string Version1 = """
        CREATE TABLE IF NOT EXISTS app_state (
            key        TEXT    NOT NULL PRIMARY KEY,
            value      TEXT    NOT NULL,
            version    INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS secret_entries (
            key        TEXT NOT NULL PRIMARY KEY,
            nonce      TEXT NOT NULL,
            ciphertext TEXT NOT NULL,
            tag        TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS sessions (
            id         TEXT    NOT NULL PRIMARY KEY,
            started_at TEXT    NOT NULL,
            ended_at   TEXT    NULL,
            status     TEXT    NOT NULL,
            data       TEXT    NOT NULL,
            updated_at TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_sessions_started_at ON sessions (started_at DESC);
        CREATE INDEX IF NOT EXISTS ix_sessions_status ON sessions (status);

        CREATE TABLE IF NOT EXISTS requests (
            id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            session_id      TEXT    NOT NULL,
            ts              TEXT    NOT NULL,
            requested_model TEXT    NULL,
            provider_id     TEXT    NULL,
            provider_model  TEXT    NULL,
            input_tokens    INTEGER NOT NULL DEFAULT 0,
            output_tokens   INTEGER NOT NULL DEFAULT 0,
            latency_ms      INTEGER NOT NULL DEFAULT 0,
            status          TEXT    NOT NULL,
            error           TEXT    NULL,
            FOREIGN KEY (session_id) REFERENCES sessions (id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_requests_session ON requests (session_id, id);
        CREATE INDEX IF NOT EXISTS ix_requests_ts ON requests (ts DESC);

        CREATE TABLE IF NOT EXISTS request_bodies (
            request_id      INTEGER NOT NULL PRIMARY KEY,
            prompt          TEXT NULL,
            request_preview TEXT NULL,
            response        TEXT NULL,
            warnings        TEXT NULL,
            FOREIGN KEY (request_id) REFERENCES requests (id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS logs (
            id      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ts      TEXT    NOT NULL,
            level   TEXT    NOT NULL,
            source  TEXT    NULL,
            message TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_logs_ts ON logs (ts DESC);

        CREATE TABLE IF NOT EXISTS provider_stats (
            provider_id      TEXT    NOT NULL PRIMARY KEY,
            requests         INTEGER NOT NULL DEFAULT 0,
            errors           INTEGER NOT NULL DEFAULT 0,
            total_latency_ms INTEGER NOT NULL DEFAULT 0,
            last_activity_at TEXT    NULL,
            last_error       TEXT    NULL
        );
        """;
}
