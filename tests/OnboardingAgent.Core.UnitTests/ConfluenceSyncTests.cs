using Microsoft.Extensions.Logging.Abstractions;
using OnboardingAgent.Core.Options;
using OnboardingAgent.Core.Sync;
using OnboardingAgent.ExternalApi.Atlassian;

namespace OnboardingAgent.Core.UnitTests;

public sealed class ConfluenceSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "confluence-sync-tests", Guid.NewGuid().ToString("N"));

    public ConfluenceSyncTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sources"));
        Directory.CreateDirectory(Path.Combine(_root, "confluence"));
        File.WriteAllText(Path.Combine(_root, "sources", "confluence-pages.json"), """
            { "pages": [
              { "pageId": "1", "documentId": "page-one", "path": "confluence/one.md" },
              { "pageId": "2", "documentId": "page-two", "path": "confluence/two.md" } ] }
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ConfluenceSyncService Service(FakeClient client) =>
        new(client, new CorpusPaths(_root, Path.Combine(_root, "manifest.json"), _root, _root), NullLogger<ConfluenceSyncService>.Instance);

    private string File1 => Path.Combine(_root, "confluence", "one.md");
    private string File2 => Path.Combine(_root, "confluence", "two.md");

    [Fact]
    public async Task SyncAsync_OnePageUnreadable_KeepsItsFileAndSyncsTheOther()
    {
        File.WriteAllText(File1, "old one");
        File.WriteAllText(File2, "old two");
        var client = new FakeClient
        {
            ["1"] = new ConfluenceException(ConfluenceFailureKind.Page, "page not found"),
            ["2"] = new ConfluencePage("2", "Two", "new two body", "oct 01, 2026", "HPS")
        };

        var result = await Service(client).SyncAsync(CancellationToken.None);

        Assert.Equal("old one", File.ReadAllText(File1));
        Assert.Contains("new two body", File.ReadAllText(File2));
        Assert.Equal(["2"], result.Synced);
        var failure = Assert.Single(result.Failed);
        Assert.Equal(("1", "page not found"), (failure.PageId, failure.Reason));
    }

    [Fact]
    public async Task SyncAsync_NoCredentials_ReturnsReasonWithoutConnectingOrTouchingFiles()
    {
        File.WriteAllText(File1, "old one");
        var client = new FakeClient { NotConfiguredReason = "no token" };

        var result = await Service(client).SyncAsync(CancellationToken.None);

        Assert.Equal("no token", result.Reason);
        Assert.Empty(result.Synced);
        Assert.Empty(result.Failed);
        Assert.False(client.Connected);
        Assert.Equal("old one", File.ReadAllText(File1));
    }

    [Fact]
    public async Task SyncAsync_AuthRejected_StopsAndKeepsEveryFile()
    {
        File.WriteAllText(File1, "old one");
        File.WriteAllText(File2, "old two");
        var client = new FakeClient { ["1"] = new ConfluenceException(ConfluenceFailureKind.Auth, "401") };

        var result = await Service(client).SyncAsync(CancellationToken.None);

        Assert.Equal("401", result.Reason);
        Assert.Equal(["1", "2"], result.Failed.Select(f => f.PageId));
        Assert.Equal(1, client.Reads);
        Assert.Equal("old one", File.ReadAllText(File1));
        Assert.Equal("old two", File.ReadAllText(File2));
    }

    [Fact]
    public async Task SyncAsync_ConnectionFails_ReportsEveryPageAndKeepsFiles()
    {
        File.WriteAllText(File1, "old one");
        var client = new FakeClient { ConnectError = new ConfluenceException(ConfluenceFailureKind.Connection, "network down") };

        var result = await Service(client).SyncAsync(CancellationToken.None);

        Assert.Equal("network down", result.Reason);
        Assert.Equal(2, result.Failed.Count);
        Assert.Equal("old one", File.ReadAllText(File1));
    }

    [Fact]
    public async Task SyncAsync_SameContentTwice_ReportsUnchangedTheSecondTime()
    {
        var client = new FakeClient
        {
            ["1"] = new ConfluencePage("1", "One", "body one", "oct 01, 2026", "HPS"),
            ["2"] = new ConfluencePage("2", "Two", "body two", null, null)
        };
        var service = Service(client);

        var first = await service.SyncAsync(CancellationToken.None);
        var second = await service.SyncAsync(CancellationToken.None);

        Assert.Equal(["1", "2"], first.Synced);
        Assert.Empty(second.Synced);
        Assert.Equal(["1", "2"], second.Unchanged);
    }

    [Fact]
    public void Sanitize_AuthorLineAssigneeColumnAndEmail_RemovesThem()
    {
        const string page = """
            # Plan
            **Author:** Jane Doe
            Owner: John Roe
            Contact ops@example.com or [mail](mailto:ops@example.com) with @jdoe.

            | Task | Assignee | Status |
            |---|---|---|
            | Migrate | Jane Doe | Done |

            SSN 123-45-6789, EIN 12-3456789. ![arch](https://x/y.png)
            ```
            @Inject public Foo foo;
            ```
            """;

        var clean = ConfluenceSanitizer.Sanitize(page);

        Assert.DoesNotContain("Jane Doe", clean);
        Assert.DoesNotContain("John Roe", clean);
        Assert.DoesNotContain("example.com", clean);
        Assert.DoesNotContain("@jdoe", clean);
        Assert.DoesNotContain("123-45-6789", clean);
        Assert.DoesNotContain("12-3456789", clean);
        Assert.Contains("| Task | Status |", clean);
        Assert.Contains("| Migrate | Done |", clean);
        Assert.Contains("[diagram omitted]", clean);
        Assert.Contains("@Inject public Foo foo;", clean);
    }

    [Fact]
    public void Parse_RovoResponse_TakesPageFieldsAndNotTheAuthor()
    {
        const string json = """
            {"content":{"nodes":[{"id":"1","title":"T","lastModified":"sept 09, 2026","space":{"key":"HPS"},
             "author":{"displayName":"Jane Doe"},"body":"text"}]}}
            """;

        var page = ConfluencePageParser.Parse("1", json);

        Assert.Equal(("T", "text", "sept 09, 2026", "HPS"), (page.Title, page.Body, page.LastModified, page.SpaceKey));
    }

    [Fact]
    public void Parse_NoNodes_ThrowsPageFailure()
    {
        var ex = Assert.Throws<ConfluenceException>(() => ConfluencePageParser.Parse("1", """{"content":{"nodes":[]}}"""));

        Assert.Equal(ConfluenceFailureKind.Page, ex.Kind);
    }

    private sealed class FakeClient : IConfluenceClient
    {
        private readonly Dictionary<string, object> _pages = [];

        public string? NotConfiguredReason { get; set; }
        public ConfluenceException? ConnectError { get; set; }
        public bool Connected { get; private set; }
        public int Reads { get; private set; }

        public object this[string pageId]
        {
            set => _pages[pageId] = value;
        }

        public Task<IConfluenceSession> ConnectAsync(CancellationToken cancellationToken)
        {
            Connected = true;
            return ConnectError is null ? Task.FromResult<IConfluenceSession>(new Session(this)) : throw ConnectError;
        }

        private sealed class Session(FakeClient owner) : IConfluenceSession
        {
            public Task<ConfluencePage> ReadPageAsync(string pageId, CancellationToken cancellationToken)
            {
                owner.Reads++;
                return owner._pages[pageId] switch
                {
                    ConfluenceException ex => throw ex,
                    ConfluencePage page => Task.FromResult(page),
                    _ => throw new InvalidOperationException()
                };
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
