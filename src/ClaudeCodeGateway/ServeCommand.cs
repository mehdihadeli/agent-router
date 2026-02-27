// using ClaudeCodeGateway.Models.Anthropic;
// using ClaudeCodeGateway.Services;
// using Spectre.Console;
// using Spectre.Console.Cli;
//
// namespace ClaudeCodeGateway;
//
//
// public class ServeCommand : Command<ServeCommand.Settings>
// {
//     public class Settings : CommandSettings
//     {
//         [CommandOption("--port")]
//         public int Port { get; set; } = 8080;
//
//         [CommandOption("--provider")]
//         public string Provider { get; set; } = "openai";
//
//         [CommandOption("--model")]
//         public string Model { get; set; } = "gpt-4o-mini";
//     }
//
//     public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
//     {
//         AnsiConsole.MarkupLine("[green]Starting Anthropic Proxy...[/]");
//
//         var builder = WebApplication.CreateBuilder();
//
//         builder.Services.AddSingleton(settings);
//         builder.Services.AddSingleton<ILLMProvider, OpenAILLMProvider>();
//         builder.Services.AddSingleton<AnthropicOpenAIAdapter>();
//
//         var app = builder.Build();
//
//         app.MapPost("/v1/messages", async (
//             AnthropicRequest request,
//             AnthropicOpenAIAdapter adapter,
//             Settings settings) =>
//         {
//             var response = await adapter.HandleMessageAsync(request, settings.Model, cancellationToken);
//             return Results.Json(response);
//         });
//
//         app.MapPost("/v1/messages/count_tokens", (
//             AnthropicRequest request,
//             AnthropicOpenAIAdapter adapter,
//             Settings settings) =>
//         {
//             var tokens = adapter.CountTokens(request, settings.Model);
//             return Results.Json(new { input_tokens = tokens });
//         });
//
//         app.Run($"http://0.0.0.0:{settings.Port}");
//
//         return 0;
//     }
// }
