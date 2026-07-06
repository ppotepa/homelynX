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
            new ErrorPresenter(),
            new TextPresenter()
        ]);
}