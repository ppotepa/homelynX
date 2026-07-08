namespace TorrentBot.Presentation;

public static class PresentationBootstrap
{
    public static ArtifactPresentation CreateDefault() =>
        new(
        [
            new SearchResultsPresenter(),
            new ConfirmationPresenter(),
            new DownloadStartedPresenter(),
            new HelpPresenter(),
            new JobsListPresenter(),
            new DownloadsListPresenter(),
            new ErrorPresenter(),
            new TextPresenter()
        ]);
}