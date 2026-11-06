using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// Regression: the automatic UseRouting ran before UseStaticFiles, so the SPA fallback endpoint matched
/// /robots.txt and /assets/*.js, StaticFileMiddleware skipped them, and index.html was returned instead.
/// These tests run in Development against the project's real wwwroot and add files after the host's build.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StaticFilesTests(TestDatabase database) : IDisposable
{
    private readonly List<string> _created = [];
    private readonly List<string> _createdDirectories = [];

    public void Dispose()
    {
        foreach (var file in _created.Where(File.Exists))
        {
            File.Delete(file);
        }

        foreach (var directory in _createdDirectories.Where(d => Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()))
        {
            Directory.Delete(directory);
        }
    }

    private string Write(string webRoot, string relativePath, string content)
    {
        var path = Path.Combine(webRoot, relativePath);
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _createdDirectories.Add(directory);
        }

        File.WriteAllText(path, content);
        _created.Add(path);
        return path;
    }

    [Fact]
    public async Task Files_added_to_wwwroot_after_build_are_served_in_Development_and_the_fallback_still_works()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(database, environment: "Development", useProjectWebRoot: true);
        using var client = factory.CreateClient();
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var id = Guid.NewGuid().ToString("N");

        // Created after the host's build, like the output of copy-to-wwwroot.mjs.
        Write(webRoot, $"probe-{id}.txt", $"probe-text-{id}");
        Write(webRoot, $"assets/probe-{id}.js", $"console.log('probe-js-{id}');");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            Write(webRoot, "index.html", "<!doctype html><html><body>spa-index</body></html>");
        }

        var text = await client.GetAsync($"/probe-{id}.txt", ct);
        text.StatusCode.Should().Be(HttpStatusCode.OK);
        text.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        (await text.Content.ReadAsStringAsync(ct)).Should().Be($"probe-text-{id}");

        var js = await client.GetAsync($"/assets/probe-{id}.js", ct);
        js.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Content.Headers.ContentType?.MediaType.Should().Contain("javascript");
        (await js.Content.ReadAsStringAsync(ct)).Should().Contain($"probe-js-{id}").And.NotContain("<html");

        var spa = await client.GetAsync("/some/unknown/route", ct);
        spa.StatusCode.Should().Be(HttpStatusCode.OK);
        spa.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        (await spa.Content.ReadAsStringAsync(ct)).Should().Contain("<html");

        var api = await client.GetAsync("/api/v1/does-not-exist", ct);
        api.StatusCode.Should().Be(HttpStatusCode.NotFound);
        api.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
