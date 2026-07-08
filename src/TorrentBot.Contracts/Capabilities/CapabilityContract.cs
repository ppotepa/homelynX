using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Contracts.Capabilities;

/// <summary>
/// Rich contract for a capability that augments CapabilityMetadata.
/// Provides exact semantics, interaction model, response construction rules,
/// and continuation rules for recursive multi-turn user responses.
/// </summary>
public sealed record CapabilityContract(
    string Name,
    string ExactSemantics,
    IReadOnlyList<ParameterSpec> Parameters,
    RiskLevel Risk,
    UserInteractionSpec? UserInteractions = null,
    ResponseConstructionSpec? ResponseSpec = null,
    IReadOnlyList<ContinuationRule>? Continuations = null,
    string? Description = null,
    string? LlmUsage = null,
    IReadOnlyList<string>? IntentHints = null,
    bool IsReadOnly = false,
    string Scope = "media");

/// <summary>
/// Describes an input parameter for the capability.
/// </summary>
public sealed record ParameterSpec(
    string Name,
    string Type, // string, int, bool, etc.
    string? Description = null,
    bool Required = false,
    object? DefaultValue = null,
    IReadOnlyList<string>? AllowedValues = null);

/// <summary>
/// Describes how the capability interacts with the user (buttons, free text, selections).
/// </summary>
public sealed record UserInteractionSpec(
    bool RequiresConfirmation = false,
    string? ConfirmationMessage = null,
    IReadOnlyList<string>? ExpectedResponseTypes = null, // e.g. "index", "confirm", "text", "cancel"
    string? PromptText = null);

/// <summary>
/// Specification for how to construct the response artifacts/presenters from execution result + context.
/// </summary>
public sealed record ResponseConstructionSpec(
    string ArtifactKind, // "search_results", "confirmation", "download_started", "text", "list", ...
    string? FormatHint = null,
    IReadOnlyList<string>? FieldsToInclude = null,
    bool UseConversationState = true,
    string? ItemsKey = null,
    string? QueryKey = null,
    string? SelectedKey = null);

/// <summary>
/// Rule describing what pending user action(s) to create after this capability succeeds,
/// based on the result or always. Enables recursive N responses.
/// </summary>
public sealed record ContinuationRule(
    string Trigger, // "always", "on_success", "when_has_results", "when_confirmation_required"
    string ActionType, // e.g. "await_select", "await_confirm", "await_indexed_choice"
    ExpectedResponseShape? ExpectedResponse = null,
    string? NextCapability = null, // optional: auto-suggest next
    string? TokenSource = "result.confirmationToken", // how to derive token from result
    string? Description = null);

/// <summary>
/// Describes the shape of user response expected for a pending action.
/// </summary>
public sealed record ExpectedResponseShape(
    string Type, // "index", "token", "yes_no", "free_text", "choice"
    string? ParameterName = null, // e.g. "index" to extract into params
    string? Regex = null,
    IReadOnlyList<string>? Examples = null,
    string? ErrorMessage = null);
