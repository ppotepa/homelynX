namespace TorrentBot.Integrations.Interfaces;

public sealed record CoordStatus(bool Online, string Service, int PendingInputs, DateTimeOffset CheckedAtUtc);

public sealed record CoordSubmitResult(bool Accepted, string? TrackingId, string? Message);

public interface ICoordInputClient
{
    Task<CoordStatus> GetStatusAsync(CancellationToken ct = default);
    Task<CoordSubmitResult> SubmitAsync(double latitude, double longitude, string? label = null, CancellationToken ct = default);
}