using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter;
using AgentRouter.Cli;
using AgentRouter.Models.Anthropic;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Routes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgentRouter.Tests;

/// <summary>
/// End-to-end integration tests that host the real gateway via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// and make real Azure OpenAI calls. Tests send requests exactly as the real Claude Code CLI does:
///
///   - Claude Code sends its built-in tools (Bash, Write, Read, etc.) — NOT custom tools.
///   - Claude Code never sends tool_choice — ToolChoice is always null.
///   - Claude Code sends multi-turn conversations with tool_use and tool_result blocks.
///
/// Required repo-root .env keys (tests skip gracefully when absent):
///   Providers:0:BaseAddress
///   Providers:0:ApiKey
///   RouteModel:DefaultModel
/// </summary>
public class EndToEndGatewayToolWiringTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Simple text-only request with no tools — verifies basic gateway roundtrip.
    /// </summary>
    [Fact]
    public async Task SimpleTextRequest_ReturnsValidAnthropicResponse()
    {
        var settings = GetTestSettings();
        if (settings is null)
            return;

        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var request = new AnthropicRequest
        {
            Model = settings.DefaultModel,
            MaxTokens = 64,
            Messages =
            [
                new Message
                {
                    Role = "user",
                    Content = new MessageContent { Text = "Say hello" },
                },
            ],
        };

        var response = await http.PostAsJsonAsync("/v1/messages", request, WebJson);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Status: {response.StatusCode}\n{body}");

        var anthropic = JsonSerializer.Deserialize<AnthropicResponse>(body, WebJson);
        Assert.NotNull(anthropic);
        Assert.NotEmpty(anthropic.Content);

        var text = anthropic.Content.OfType<ResponseText>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.False(string.IsNullOrWhiteSpace(text.Text));
    }

    /// <summary>
    /// Sends Claude Code's built-in Bash tool (no tool_choice) — exactly as the real CLI does.
    /// Claude Code provides tools like Bash/Write/Read but lets the model decide when to call them.
    /// The model should respond with a tool_use for Bash containing a mkdir command.
    /// Then we execute it and send tool_result back — verifying the full multi-turn flow.
    /// </summary>
    /// <remarks>
    /// LIVE, NON-DETERMINISTIC: this test calls a real provider and asserts on the model's own tool
    /// choice. We deliberately do not send <c>tool_choice</c> (mirroring Claude Code), so a model may
    /// legitimately return a different Bash command (e.g. <c>uname -a</c>) and the directory will not
    /// be created, failing the on-disk assertion even though the gateway wired tools correctly.
    /// The gateway contract under test — tool definitions forwarded, <c>tool_use</c> parsed,
    /// <c>tool_result</c> accepted on turn 2 — is covered deterministically by the Api.Tests.
    /// </remarks>
    [Fact]
    public async Task ClaudeCodeBashTool_NoToolChoice_ModelCallsBash_DirectoryCreatedOnDisk()
    {
        var settings = GetTestSettings();
        if (settings is null)
            return;

        var tempRoot = Path.Combine(Path.GetTempPath(), "ccg-e2e-" + Guid.NewGuid().ToString("N"));
        var targetDir = Path.Combine(tempRoot, "TestApp");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var factory = CreateFactory();
            using var http = factory.CreateClient();

            // Claude Code built-in tool schemas (aligned with the real tool names Claude Code sends).
            // We keep a realistic subset needed for this test.
            var bashSchema = JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new
                    {
                        command = new { type = "string", description = "The command to execute" },
                        timeout = new
                        {
                            type = "integer",
                            description = "Optional timeout in milliseconds",
                        },
                    },
                    required = new[] { "command" },
                }
            );

            var readSchema = JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new
                    {
                        file_path = new
                        {
                            type = "string",
                            description = "The absolute path to the file to read",
                        },
                        offset = new
                        {
                            type = "integer",
                            description = "The line number to start reading from",
                        },
                        limit = new
                        {
                            type = "integer",
                            description = "Maximum number of lines to read",
                        },
                    },
                    required = new[] { "file_path" },
                }
            );

            var editSchema = JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new
                    {
                        file_path = new
                        {
                            type = "string",
                            description = "The absolute path to the file to modify",
                        },
                        old_string = new
                        {
                            type = "string",
                            description = "The exact string to replace",
                        },
                        new_string = new
                        {
                            type = "string",
                            description = "The replacement string",
                        },
                    },
                    required = new[] { "file_path", "old_string", "new_string" },
                }
            );

            var writeSchema = JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new
                    {
                        file_path = new
                        {
                            type = "string",
                            description = "The absolute path to the file to write",
                        },
                        content = new { type = "string", description = "Content to write" },
                    },
                    required = new[] { "file_path", "content" },
                }
            );

            var globSchema = JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new { type = "string", description = "Glob pattern to match" },
                        path = new { type = "string", description = "Root path" },
                    },
                    required = new[] { "pattern" },
                }
            );

            var grepSchema = JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new
                    {
                        pattern = new { type = "string", description = "Regex pattern" },
                        path = new { type = "string", description = "Root path" },
                    },
                    required = new[] { "pattern" },
                }
            );

            var tools = new List<Tool>
            {
                new Tool
                {
                    Name = "Bash",
                    Description = "Executes a given bash command and returns its output.",
                    InputSchema = bashSchema,
                },
                new Tool
                {
                    Name = "Read",
                    Description = "Reads a file from the local filesystem.",
                    InputSchema = readSchema,
                },
                new Tool
                {
                    Name = "Edit",
                    Description = "Performs exact string replacements in files.",
                    InputSchema = editSchema,
                },
                new Tool
                {
                    Name = "Write",
                    Description = "Writes a file to the local filesystem.",
                    InputSchema = writeSchema,
                },
                new Tool
                {
                    Name = "Glob",
                    Description = "Fast file pattern matching.",
                    InputSchema = globSchema,
                },
                new Tool
                {
                    Name = "Grep",
                    Description = "Search tool built on ripgrep.",
                    InputSchema = grepSchema,
                },
            };

            // ── Turn-1: send tools with NO tool_choice (real Claude Code behavior) ─
            // We do NOT tell the model which tool to use — it decides on its own.
            var turn1 = new AnthropicRequest
            {
                Model = settings.DefaultModel,
                MaxTokens = 512,
                Messages =
                [
                    new Message
                    {
                        Role = "user",
                        Content = new MessageContent
                        {
                            Text = $"Create a directory at the path: {targetDir}",
                        },
                    },
                ],
                Tools = tools,
                ToolChoice = null,
            };

            var resp1 = await http.PostAsJsonAsync("/v1/messages", turn1, WebJson);
            var resp1Body = await resp1.Content.ReadAsStringAsync();
            Assert.True(
                resp1.IsSuccessStatusCode,
                $"Turn-1 failed: {resp1.StatusCode}\n{resp1Body}"
            );

            var anth1 = JsonSerializer.Deserialize<AnthropicResponse>(resp1Body, WebJson);
            Assert.NotNull(anth1);

            // Model should have decided to call a tool.
            var toolUse = anth1.Content.OfType<ResponseToolUse>().FirstOrDefault();
            Assert.NotNull(toolUse);

            // ── Execute the tool locally (exactly what Claude Code CLI does) ─────
            // We run whatever command the AI chose — we do NOT hardcode the action.
            var toolOutput = await ExecuteToolAsync(toolUse);

            // ── Turn-2: send tool_result back ────────────────────────────────────
            var turn2 = new AnthropicRequest
            {
                Model = settings.DefaultModel,
                MaxTokens = 256,
                Messages =
                [
                    new Message
                    {
                        Role = "user",
                        Content = new MessageContent
                        {
                            Text = $"Create a directory at the path: {targetDir}",
                        },
                    },
                    new Message
                    {
                        Role = "assistant",
                        Content = new MessageContent
                        {
                            Blocks =
                            [
                                new ToolUseBlock
                                {
                                    Id = toolUse.Id,
                                    Name = toolUse.Name,
                                    Input = toolUse.Input,
                                },
                            ],
                        },
                    },
                    new Message
                    {
                        Role = "user",
                        Content = new MessageContent
                        {
                            Blocks =
                            [
                                new ToolResultBlock
                                {
                                    ToolUseId = toolUse.Id,
                                    Content = toolOutput,
                                },
                            ],
                        },
                    },
                ],
                Tools = tools,
                ToolChoice = null,
            };

            var resp2 = await http.PostAsJsonAsync("/v1/messages", turn2, WebJson);
            var resp2Body = await resp2.Content.ReadAsStringAsync();
            Assert.True(
                resp2.IsSuccessStatusCode,
                $"Turn-2 failed: {resp2.StatusCode}\n{resp2Body}"
            );

            // ── Assert directory exists on disk ──────────────────────────────────
            Assert.True(
                Directory.Exists(targetDir),
                $"Expected '{targetDir}' to exist after tool execution.\n"
                    + $"Tool used: {toolUse.Name}\n"
                    + $"Tool input: {toolUse.Input.GetRawText()}\n"
                    + $"Tool output: {toolOutput}\n"
                    + $"Turn-1 body: {resp1Body}"
            );
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            { /* ignore */
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tool execution — mirrors what Claude Code CLI does locally
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the tool the AI model chose. This is exactly what Claude Code does:
    /// it reads the tool name + input from the response and runs it locally.
    /// </summary>
    private static async Task<string> ExecuteToolAsync(ResponseToolUse toolUse)
    {
        return toolUse.Name switch
        {
            "Bash" => await ExecuteBashAsync(toolUse.Input),
            "Write" => ExecuteWrite(toolUse.Input),
            "Read" => ExecuteRead(toolUse.Input),
            "Edit" => ExecuteEdit(toolUse.Input),
            "Glob" => ExecuteGlob(toolUse.Input),
            "Grep" => ExecuteGrep(toolUse.Input),
            _ => $"Unknown tool: {toolUse.Name}",
        };
    }

    private static async Task<string> ExecuteBashAsync(JsonElement input)
    {
        var command = input.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(command))
        {
            command = input.TryGetProperty("_", out var underscoreEl)
                ? underscoreEl.GetString() ?? ""
                : "";
        }
        if (string.IsNullOrWhiteSpace(command))
            return "Error: empty command";

        var isWindows = OperatingSystem.IsWindows();
        if (isWindows)
        {
            // Normalize common bash flags that cmd.exe doesn't understand.
            if (command.StartsWith("mkdir -p ", StringComparison.OrdinalIgnoreCase))
            {
                command = "mkdir " + command[9..];
            }
        }

        // On Windows, run via cmd.exe; on Unix, run via /bin/bash.
        var (shell, args) = isWindows
            ? ("cmd.exe", $"/c {command}")
            : ("/bin/bash", $"-c \"{command.Replace("\"", "\\\"")}\"");

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? string.IsNullOrWhiteSpace(stdout)
                ? "(success)"
                : stdout.Trim()
            : $"Exit code {process.ExitCode}: {stderr.Trim()}";
    }

    private static string ExecuteWrite(JsonElement input)
    {
        var filePath = input.TryGetProperty("file_path", out var fpEl) ? fpEl.GetString() : null;
        var content = input.TryGetProperty("content", out var cEl) ? cEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(filePath))
            return "Error: missing file_path";

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, content ?? "");
        return $"File written to {filePath}";
    }

    private static string ExecuteRead(JsonElement input)
    {
        var filePath = input.TryGetProperty("file_path", out var fpEl) ? fpEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return "Error: file not found";

        return File.ReadAllText(filePath);
    }

    private static string ExecuteEdit(JsonElement input)
    {
        var filePath = input.TryGetProperty("file_path", out var fpEl) ? fpEl.GetString() : null;
        var oldString = input.TryGetProperty("old_string", out var osEl) ? osEl.GetString() : null;
        var newString = input.TryGetProperty("new_string", out var nsEl) ? nsEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return "Error: file not found";
        if (oldString is null || newString is null)
            return "Error: missing old_string/new_string";

        var text = File.ReadAllText(filePath);
        File.WriteAllText(filePath, text.Replace(oldString, newString));
        return "(success)";
    }

    private static string ExecuteGlob(JsonElement input)
    {
        // Minimal implementation: just acknowledge. For this e2e, we don't need full glob.
        return "(not-implemented)";
    }

    private static string ExecuteGrep(JsonElement input)
    {
        // Minimal implementation: just acknowledge. For this e2e, we don't need full grep.
        return "(not-implemented)";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static TestSettings? GetTestSettings()
    {
        var config = new ConfigurationBuilder().AddDotEnvFile(optional: true).Build();
        var options = new ClaudeGatewayOptions();
        config.Bind(options);

        if (
            options.RouteModel is null
            || string.IsNullOrWhiteSpace(options.RouteModel.DefaultModel)
        )
            return null;

        ModelReference modelReference;
        try
        {
            modelReference = RouteModelFactory.Resolve(options, options.RouteModel.DefaultModel);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // .env is absent, incomplete, or has a malformed route — skip this live test.
            return null;
        }

        if (string.IsNullOrWhiteSpace(modelReference.Provider.BaseAddress))
            return null;

        if (string.IsNullOrWhiteSpace(modelReference.Provider.ApiKey))
            return null;

        if (
            modelReference.Provider.BaseAddress.Contains(
                "your-resource-name",
                StringComparison.OrdinalIgnoreCase
            )
            || modelReference.Provider.ApiKey.Contains(
                "replace-with-real-api-key",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return null;

        return new TestSettings(options.RouteModel.DefaultModel);
    }

    private static WebApplicationFactory<GatewayHost> CreateFactory()
    {
        return new GatewayFactory();
    }

    private sealed class GatewayFactory : WebApplicationFactory<GatewayHost>
    {
        protected override IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(builder => builder.UseTestServer());

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var applicationBuilder = GatewayApplication.CreateBuilder(["--environment", "Testing"]);
            applicationBuilder.WebHost.UseTestServer();
            var app = GatewayApplication.Build(applicationBuilder);
            app.Start();
            return app;
        }
    }

    private sealed record TestSettings(string DefaultModel);
}
