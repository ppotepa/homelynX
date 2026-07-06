using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Integrations.Fakes;

public sealed class FakeCoordInputClient : ICoordInputClient
{
    private int _pending;

    public Task<CoordStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new CoordStatus(true, "coord-input", _pending, DateTimeOffset.UtcNow));

    public Task<CoordSubmitResult> SubmitAsync(double latitude, double longitude, string? label = null, CancellationToken ct = default)
    {
        _pending++;
        return Task.FromResult(new CoordSubmitResult(
            true,
            $"coord-{Guid.NewGuid():N}"[..12],
            $"Accepted {label ?? "point"} at {latitude},{longitude}"));
    }
}