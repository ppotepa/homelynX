using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine.Pipeline.ResponseArtifacts;
using TorrentBot.Plugins.Downloads.Capabilities;
using TorrentBot.Plugins.Torrent.Capabilities;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ResponseArtifactBuildersTests
{
    [Fact]
    public void List_builder_formats_from_contract_spec_not_handler_message()
    {
        var spec = DownloadContracts.List.ResponseSpec!;
        Assert.Equal("downloads", spec.ItemsKey);
        var result = new ExecutionResult(
            Success: true,
            CapabilityResult: new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?>
                {
                    ["downloads"] = new List<Dictionary<string, object?>>
                    {
                        new(StringComparer.Ordinal)
                        {
                            ["name"] = "ubuntu.iso",
                            ["status"] = "downloading",
                            ["progress"] = "42",
                            ["dlspeed"] = "1048576",
                            ["eta"] = "120"
                        }
                    }
                },
                Message: "IGNORED_HANDLER_MESSAGE"));

        var items = ResponseArtifactBuilders.Build(spec, result);
        var text = Assert.IsType<TextArtifact>(items[0]);
        Assert.Contains("ubuntu.iso", text.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORED_HANDLER_MESSAGE", text.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_results_builder_produces_structured_artifact_from_spec_path()
    {
        var spec = TorrentContracts.Search.ResponseSpec!;
        var result = new ExecutionResult(
            Success: true,
            CapabilityResult: new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?>
                {
                    ["artifactKind"] = "search_results",
                    ["query"] = "debian",
                    ["totalCount"] = 1,
                    ["count"] = 1,
                    ["page"] = 0,
                    ["pageSize"] = 5,
                    ["hasMore"] = false,
                    ["totalPages"] = 1,
                    ["results"] = new List<Dictionary<string, object?>>
                    {
                        new(StringComparer.Ordinal)
                        {
                            ["index"] = 1,
                            ["name"] = "Debian 12 ISO",
                            ["sizeBytes"] = 1_073_741_824L,
                            ["seeders"] = 42
                        }
                    }
                },
                Message: "IGNORED_HANDLER_MESSAGE"));

        var items = ResponseArtifactBuilders.Build(spec, result);
        var search = Assert.IsType<SearchResultsArtifact>(items[0]);
        Assert.Equal("debian", search.Query);
        Assert.Single(search.Items);
        Assert.Equal("Debian 12 ISO", search.Items[0].Name);

        var formatted = SearchResultsFormatting.FormatPlain(search);
        Assert.Contains("Debian 12 ISO", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORED_HANDLER_MESSAGE", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmation_builder_uses_result_data_not_presenter_hardcoding()
    {
        var spec = DownloadContracts.Start.ResponseSpec!;
        var result = new ExecutionResult(
            Success: false,
            CapabilityResult: new CapabilityResult(
                Success: false,
                Data: new Dictionary<string, object?>
                {
                    ["confirmationRequired"] = true,
                    ["confirmationToken"] = "tok-abc",
                    ["capabilityName"] = "download.start"
                },
                Message: "Start this download?"));

        var items = ResponseArtifactBuilders.Build(spec, result);
        var confirm = Assert.IsType<ConfirmationArtifact>(items[0]);
        Assert.Equal("tok-abc", confirm.Token);
        Assert.Equal("Start this download?", confirm.Message);
    }

    [Fact]
    public void Download_started_builder_produces_structured_artifact()
    {
        var spec = TorrentContracts.SelectResult.ResponseSpec!;
        var result = new ExecutionResult(
            Success: true,
            CapabilityResult: new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?>
                {
                    ["selected"] = new Dictionary<string, object?> { ["name"] = "My Torrent" },
                    ["provider"] = "torrent",
                    ["jobId"] = "job-1",
                    ["ticket"] = new Dictionary<string, object?> { ["downloadId"] = "dl-1" }
                },
                Message: "IGNORED"));

        var items = ResponseArtifactBuilders.Build(spec, result);
        var started = Assert.IsType<DownloadStartedArtifact>(items[0]);
        Assert.Equal("My Torrent", started.Name);
        Assert.Equal("torrent", started.Provider);

        var formatted = DownloadStartedFormatting.FormatMessage(
            started.Name,
            started.Provider,
            started.JobId,
            started.DownloadId);
        Assert.Contains("My Torrent", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORED", formatted, StringComparison.Ordinal);
    }
}