using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;

namespace TorrentBot.Integrations.Fakes;

public sealed class FakeJackettClient : IJackettClient
{
    private IReadOnlyList<TorrentSearchResult> _results = [];

    public void SetResults(IEnumerable<TorrentSearchResult> results) =>
        _results = results.ToList();

    public Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<TorrentSearchResult>>([]);
        }

        var matches = _results
            .Where(r => r.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || query.Contains(r.Title, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<TorrentSearchResult>>(matches.Count > 0 ? matches : _results);
    }
}