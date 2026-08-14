namespace TorrentBot.Contracts.Capabilities;

public static class CapabilityContractExtensions
{
    public static CapabilityMetadata ToMetadata(this CapabilityContract contract, string? command = null) =>
        new(
            Name: contract.Name,
            Command: command,
            Description: contract.Description ?? contract.ExactSemantics,
            Permission: contract.Risk is RiskLevel.Admin ? "ADMIN" : "USER",
            Risk: contract.Risk,
            IsReadOnly: contract.IsReadOnly,
            Scope: contract.Scope);

    public static CapabilityContract WithCommand(this CapabilityContract contract, string? command) =>
        contract;
}
