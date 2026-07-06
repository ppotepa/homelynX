using System.CommandLine;
using System.Text.Json;
using TorrentBot.Acl;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Query;
using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine;
using TorrentBot.Llm;
using TorrentBot.Presentation;

namespace TorrentBot.Adapters.Cli;

public sealed class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static RootCommand BuildRootCommand(Func<EngineHost>? engineFactory = null)
    {
        var root = new RootCommand("TorrentBot2 diagnostic CLI");
        var dryRunOption = new Option<bool>("--dry-run", "Simulate without persisting side effects");
        var jsonOption = new Option<bool>("--json", "Emit JSON output");
        var userOption = new Option<string>("--user", () => "cli-user", "Actor user id for ACL context");
        var confirmOption = new Option<string?>("--confirm", "Confirmation token for destructive capabilities");

        var capabilityCall = new Command("call", "Invoke a capability by name");
        var capabilityNameArg = new Argument<string>("name", "Capability name");
        var paramOption = new Option<string[]>("--param", "Parameter as key=value") { AllowMultipleArgumentsPerToken = true };
        capabilityCall.AddArgument(capabilityNameArg);
        capabilityCall.AddOption(dryRunOption);
        capabilityCall.AddOption(jsonOption);
        capabilityCall.AddOption(userOption);
        capabilityCall.AddOption(paramOption);
        capabilityCall.AddOption(confirmOption);
        capabilityCall.SetHandler(async (name, dryRun, json, userId, paramPairs, confirmToken) =>
        {
            Environment.ExitCode = await RunCapabilityAsync(name, dryRun, json, userId, ParseParams(paramPairs, confirmToken), engineFactory);
        }, capabilityNameArg, dryRunOption, jsonOption, userOption, paramOption, confirmOption);

        var capabilityRoot = new Command("capability", "Capability operations");
        capabilityRoot.AddCommand(capabilityCall);

        var listCmd = new Command("list", "List registered capabilities");
        listCmd.AddOption(jsonOption);
        listCmd.AddOption(userOption);
        listCmd.SetHandler(async (json, userId) =>
        {
            Environment.ExitCode = await RunCapabilitiesListAsync(json, userId, engineFactory);
        }, jsonOption, userOption);

        var queryCmd = new Command("query", "Execute query.execute");
        var sourceArg = new Argument<string>("source");
        var whereOption = new Option<string?>("--where", "Filter expression field=op:value");
        queryCmd.AddArgument(sourceArg);
        queryCmd.AddOption(whereOption);
        queryCmd.AddOption(jsonOption);
        queryCmd.AddOption(userOption);
        queryCmd.SetHandler(async (source, where, json, userId) =>
        {
            Environment.ExitCode = await RunQueryAsync(source, where, json, userId, engineFactory);
        }, sourceArg, whereOption, jsonOption, userOption);

        var agentPlan = new Command("plan", "Plan natural-language request");
        var textArg = new Argument<string>("text");
        agentPlan.AddArgument(textArg);
        agentPlan.AddOption(dryRunOption);
        agentPlan.AddOption(jsonOption);
        agentPlan.AddOption(userOption);
        agentPlan.SetHandler(async (text, dryRun, json, userId) =>
        {
            Environment.ExitCode = await RunAgentAsync(text, dryRun, json, userId, execute: false, engineFactory);
        }, textArg, dryRunOption, jsonOption, userOption);

        var agentRun = new Command("run", "Execute natural-language request");
        agentRun.AddArgument(textArg);
        agentRun.AddOption(dryRunOption);
        agentRun.AddOption(jsonOption);
        agentRun.AddOption(userOption);
        agentRun.SetHandler(async (text, dryRun, json, userId) =>
        {
            Environment.ExitCode = await RunAgentAsync(text, dryRun, json, userId, execute: true, engineFactory);
        }, textArg, dryRunOption, jsonOption, userOption);

        var agentRoot = new Command("agent", "LLM agent operations");
        agentRoot.AddCommand(agentPlan);
        agentRoot.AddCommand(agentRun);

        root.AddCommand(capabilityRoot);
        root.AddCommand(new Command("capabilities", "Capability registry") { listCmd });
        root.AddCommand(queryCmd);
        root.AddCommand(agentRoot);
        return root;
    }

    public static Task<int> RunAsync(string[] args, Func<EngineHost>? engineFactory = null) =>
        BuildRootCommand(engineFactory).InvokeAsync(args);

    internal static async Task<int> RunCapabilityAsync(
        string capabilityName, bool dryRun, bool json, string userId,
        IReadOnlyDictionary<string, object?> parameters, Func<EngineHost>? engineFactory = null)
    {
        await using var scope = await StartEngineAsync(engineFactory, userId);
        var pipelineResult = await scope.Pipeline.RunAsync(
            BuildInvocation(capabilityName, dryRun, scope.User, parameters));
        return WritePipelineResult(pipelineResult, json);
    }

    internal static async Task<int> RunCapabilitiesListAsync(bool json, string userId, Func<EngineHost>? engineFactory = null)
    {
        await using var scope = await StartEngineAsync(engineFactory, userId);
        var result = await scope.Engine.SubmitAsync(BuildInvocation("system.capabilities", false, scope.User, null));
        if (!result.Success)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result.CapabilityResult?.Data, JsonOptions));
        }

        return 0;
    }

    internal static async Task<int> RunQueryAsync(
        string source, string? where, bool json, string userId, Func<EngineHost>? engineFactory = null)
    {
        var parameters = new Dictionary<string, object?> { ["source"] = source };
        if (!string.IsNullOrWhiteSpace(where) && TryParseWhereExpression(where, out var clause))
        {
            parameters["where"] = new[] { clause };
        }

        return await RunCapabilityAsync("query.execute", false, json, userId, parameters, engineFactory);
    }

    public static bool TryParseWhereExpression(string expression, out Dictionary<string, object?> clause)
    {
        clause = new Dictionary<string, object?>(StringComparer.Ordinal);
        var eqIndex = expression.IndexOf('=');
        if (eqIndex > 0)
        {
            var field = expression[..eqIndex];
            var remainder = expression[(eqIndex + 1)..];
            var colonIndex = remainder.IndexOf(':');
            if (colonIndex > 0)
            {
                clause["field"] = field;
                clause["op"] = remainder[..colonIndex];
                clause["value"] = remainder[(colonIndex + 1)..];
                return true;
            }
        }

        var parts = expression.Split(':', 3);
        if (parts.Length == 3)
        {
            clause["field"] = parts[0];
            clause["op"] = parts[1];
            clause["value"] = parts[2];
            return true;
        }

        return false;
    }

    internal static async Task<int> RunAgentAsync(
        string text, bool dryRun, bool json, string userId, bool execute, Func<EngineHost>? engineFactory = null)
    {
        await using var scope = await StartEngineAsync(engineFactory, userId);
        var invocation = new Invocation
        {
            IsExplicit = false,
            Text = text,
            IsDryRun = execute ? dryRun : true,
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), userId, source: "cli"),
            User = scope.User
        };
        var pipelineResult = await scope.Pipeline.RunAsync(invocation);
        return WritePipelineResult(pipelineResult, json);
    }

    private static int WritePipelineResult(PipelineResult result, bool json)
    {
        var presentation = PresentationBootstrap.CreateDefault();
        var rendered = presentation.Render(
            result.Artifacts,
            new RenderContext(RenderChannel.Cli, json ? RenderFormat.Json : RenderFormat.Plain));

        if (json && !string.IsNullOrWhiteSpace(rendered.Json))
        {
            Console.WriteLine(rendered.Json);
        }
        else if (result.Success)
        {
            Console.WriteLine(rendered.Text);
        }
        else
        {
            Console.Error.WriteLine(rendered.Text);
        }

        return rendered.ExitCode;
    }

    private static int WriteExecutionResult(ExecutionResult result, bool json)
    {
        var artifacts = Engine.Pipeline.ArtifactAccumulator.FromExecutionResult(result);
        return WritePipelineResult(new PipelineResult(result.Success, artifacts, new ExecutionPlan(PlanSource.Deterministic, []), result.Error), json);
    }

    private static async Task<IReadOnlyList<TorrentBot.Contracts.Capabilities.CapabilityMetadata>> LoadCapabilitiesAsync(
        EngineHost engine, UserContext user)
    {
        var result = await engine.SubmitAsync(BuildInvocation("system.capabilities", false, user, null));
        if (!result.Success
            || result.CapabilityResult?.Data is not Dictionary<string, object?> data
            || data["capabilities"] is not List<Dictionary<string, object?>> capabilities)
        {
            return [];
        }

        return capabilities.Select(item => new TorrentBot.Contracts.Capabilities.CapabilityMetadata(
            item["name"]?.ToString() ?? "unknown",
            item.TryGetValue("command", out var cmd) ? cmd?.ToString() : null,
            item.TryGetValue("description", out var desc) ? desc?.ToString() ?? string.Empty : string.Empty,
            item.TryGetValue("permission", out var perm) ? perm?.ToString() ?? "USER" : "USER",
            TorrentBot.Contracts.Capabilities.RiskLevel.Safe)).ToList();
    }

    private static async Task<EngineScope> StartEngineAsync(Func<EngineHost>? engineFactory, string userId)
    {
        var acl = AclService.FromEnvironment();
        var user = acl.ResolveUser(userId);
        var engine = engineFactory?.Invoke() ?? EngineBootstrap.Create(aclService: acl);
        await engine.StartAsync();
        var pipeline = PipelineBootstrap.Create(engine, engine.LlmPipeline);
        return new EngineScope(engine, user, pipeline);
    }

    private static Invocation BuildInvocation(
        string capabilityName, bool dryRun, UserContext user, IReadOnlyDictionary<string, object?>? parameters) =>
        new()
        {
            IsExplicit = true,
            CapabilityName = capabilityName,
            Parameters = parameters,
            IsDryRun = dryRun,
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), user.UserId, source: "cli"),
            User = user
        };

    private static Dictionary<string, object?> ParseParams(string[]? pairs, string? confirmToken = null)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (pairs is not null)
        {
            foreach (var pair in pairs)
            {
                var idx = pair.IndexOf('=');
                if (idx > 0) result[pair[..idx]] = pair[(idx + 1)..];
            }
        }

        if (!string.IsNullOrWhiteSpace(confirmToken))
        {
            result["confirmationToken"] = confirmToken;
        }

        return result;
    }

    private sealed class EngineScope(EngineHost engine, UserContext user, IInvocationPipeline pipeline) : IAsyncDisposable
    {
        public EngineHost Engine => engine;
        public UserContext User => user;
        public IInvocationPipeline Pipeline => pipeline;
        public async ValueTask DisposeAsync() => await engine.StopAsync();
    }
}