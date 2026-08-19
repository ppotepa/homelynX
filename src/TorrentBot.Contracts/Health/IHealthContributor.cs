namespace TorrentBot.Contracts.Health;

public interface IHealthContributor
{
    string Name { get; }
    Task<HealthContribution> CheckAsync(CancellationToken ct=default);
}

public sealed record HealthContribution(string Status,string Detail);
