namespace TorrentBot.Contracts.Capabilities;

/// <summary>
/// Contract for a capability: parameters, interaction rules, response construction,
/// and optional multi-turn continuation rules.
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
    bool IsReadOnly = false,
    string Scope = "media");

public sealed record ParameterSpec(
    string Name,
    string Type,
    string? Description = null,
    bool Required = false,
    object? DefaultValue = null,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record UserInteractionSpec(
    bool RequiresConfirmation = false,
    string? ConfirmationMessage = null,
    IReadOnlyList<string>? ExpectedResponseTypes = null,
    string? PromptText = null);

public sealed record ResponseConstructionSpec(
    string ArtifactKind,
    string? FormatHint = null,
    IReadOnlyList<string>? FieldsToInclude = null,
    bool UseConversationState = true,
    string? ItemsKey = null,
    string? QueryKey = null,
    string? SelectedKey = null);

public sealed record ContinuationRule(
    string Trigger,
    string ActionType,
    ExpectedResponseShape? ExpectedResponse = null,
    string? NextCapability = null,
    string? TokenSource = "result.confirmationToken",
    string? Description = null);

public sealed record ExpectedResponseShape(
    string Type,
    string? ParameterName = null,
    string? Regex = null,
    IReadOnlyList<string>? Examples = null,
    string? ErrorMessage = null);
