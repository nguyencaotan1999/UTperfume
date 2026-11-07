using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UTperfume.Api.Infrastructure;
using UTperfume.Infrastructure.Identity;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

/// <summary>
/// P4-02: the <c>bootstrap-admin</c> command (ADR-0010 section 6). It creates the first <c>Catalog.Admin</c> only when no
/// admin exists, takes no password on the command line, and never logs or prints the password. 0002 BR-A-01 and BR-A-31.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BootstrapAdminCommandTests : IAsyncLifetime
{
    private const string Email = "bootstrap.admin@example.test";
    private const string Secret = "Bootstrap-Secret-Marker-91";

    private readonly TestDatabase _database;

    public BootstrapAdminCommandTests(TestDatabase database) => _database = database;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await AdminIdentityTestSupport.ResetIdentityAsync(_database);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ApiFactory NewFactory(
        string environment = "Testing", string? email = Email, string? password = Secret, bool allowNonDevelopment = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Bootstrap:AdminEmail"] = email,
            ["Bootstrap:AdminPassword"] = password,
            ["Bootstrap:AdminDisplayName"] = "Bootstrap Admin",
            ["Bootstrap:AllowNonDevelopment"] = allowNonDevelopment ? "true" : null,
            ["RateLimiting:AuthPerMinute"] = "1000",
        };
        return new ApiFactory(_database, settings, environment, useTestAuthentication: false);
    }

    private static async Task<(int Exit, string Output)> RunAsync(
        ApiFactory factory, string[]? args = null, Func<string, bool, string?>? prompt = null)
    {
        var output = new StringWriter();
        var exit = await BootstrapAdminCommand.RunAsync(
            args ?? [BootstrapAdminCommand.Name],
            factory.Services,
            output,
            prompt ?? ((_, _) => throw new InvalidOperationException("The command must not prompt when configuration is complete.")),
            Ct);
        return (exit, output.ToString());
    }

    private async Task<List<AdminUser>> AllUsersAsync()
    {
        await using var db = _database.CreateContext();
        return await db.Users.AsNoTracking().ToListAsync(Ct);
    }

    [Fact]
    public async Task BR_A_01_BootstrapCreatesTheFirstAdminFromConfiguration_WhoCanThenSignIn()
    {
        using var factory = NewFactory();

        var (exit, output) = await RunAsync(factory);

        exit.Should().Be(BootstrapAdminCommand.Ok);
        var user = (await AllUsersAsync()).Should().ContainSingle().Subject;
        user.Email.Should().Be(Email);
        user.DisplayName.Should().Be("Bootstrap Admin");
        user.IsActive.Should().BeTrue();
        user.LockoutEnabled.Should().BeTrue();
        user.PasswordHash.Should().NotBeNullOrEmpty().And.NotContain(Secret);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            (await users.GetRolesAsync(user)).Should().Equal("Catalog.Admin");
        }

        using var client = factory.CreateSecureClient();
        using var signIn = await client.SignInAsync(Email, Secret, Ct);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        output.Should().NotContain(Secret);
    }

    [Fact]
    public async Task BR_A_31_BootstrapNeverWritesThePasswordToTheLogOrTheOutput()
    {
        using var factory = NewFactory();

        var (_, output) = await RunAsync(factory);

        output.Should().NotContain(Secret);
        var logs = string.Join("\n", factory.Logs.Entries.Select(e => e.Message + " " + string.Join(" ", e.State.Values)));
        logs.Should().NotContain(Secret);
        logs.Should().Contain("bootstrap_admin");
    }

    [Fact]
    public async Task BR_A_01_BootstrapDoesNothingWhenAnAdminAlreadyExists()
    {
        using var factory = NewFactory();
        await AdminIdentityTestSupport.CreateUserAsync(factory.Services, "existing@example.test");

        var (exit, output) = await RunAsync(factory);

        exit.Should().Be(BootstrapAdminCommand.Ok);
        output.Should().Contain("already exists");
        (await AllUsersAsync()).Should().ContainSingle(u => u.Email == "existing@example.test");
    }

    [Fact]
    public async Task BR_A_01_BootstrapIsIdempotent_ASecondRunCreatesNothing()
    {
        using var factory = NewFactory();

        await RunAsync(factory);
        var (exit, _) = await RunAsync(factory);

        exit.Should().Be(BootstrapAdminCommand.Ok);
        (await AllUsersAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task BR_A_01_BootstrapRefusesAnyCommandLineArgument_SoAPasswordCanNeverBePassedOnIt()
    {
        using var factory = NewFactory(password: null);

        var (exit, output) = await RunAsync(
            factory, [BootstrapAdminCommand.Name, "--Bootstrap:AdminPassword=" + Secret]);

        exit.Should().Be(BootstrapAdminCommand.BadUsage);
        output.Should().Contain("accepts no arguments").And.NotContain(Secret);
        (await AllUsersAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(new[] { "bootstrap-admin" }, true, true)]
    [InlineData(new[] { "bootstrap-admin", "--x=1" }, true, false)]
    [InlineData(new[] { "bootstrap-admin", "secret" }, true, false)]
    [InlineData(new string[0], false, false)]
    [InlineData(new[] { "--urls", "http://localhost:1" }, false, false)]
    public void BR_A_01_OnlyTheBareCommandNameIsAccepted(string[] args, bool requested, bool bare)
    {
        BootstrapAdminCommand.IsRequested(args).Should().Be(requested);
        BootstrapAdminCommand.HasOnlyTheCommandName(args).Should().Be(bare);
    }

    [Fact]
    public async Task BR_A_01_BootstrapReportsPolicyFailuresWithoutEchoingThePasswordAndCreatesNothing()
    {
        const string weak = "Short-1a";
        using var factory = NewFactory(password: weak);

        var (exit, output) = await RunAsync(factory);

        exit.Should().Be(BootstrapAdminCommand.Failed);
        output.Should().Contain("could not be created").And.NotContain(weak);
        (await AllUsersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_01_BootstrapPromptsWhenConfigurationIsMissing_AndMasksThePassword()
    {
        using var factory = NewFactory(email: null, password: null);
        var asked = new List<(string Label, bool Secret)>();
        var answers = new Queue<string?>([Email, Secret, Secret]);

        var (exit, output) = await RunAsync(factory, prompt: (label, secret) =>
        {
            asked.Add((label, secret));
            return answers.Dequeue();
        });

        exit.Should().Be(BootstrapAdminCommand.Ok);
        asked.Select(a => a.Secret).Should().Equal(false, true, true);
        output.Should().NotContain(Secret);
        (await AllUsersAsync()).Should().ContainSingle(u => u.Email == Email);
    }

    [Fact]
    public async Task BR_A_01_BootstrapRefusesMismatchedPasswordConfirmations()
    {
        using var factory = NewFactory(email: null, password: null);
        var answers = new Queue<string?>([Email, Secret, "Different-Passw0rd-1"]);

        var (exit, _) = await RunAsync(factory, prompt: (_, _) => answers.Dequeue());

        exit.Should().Be(BootstrapAdminCommand.Failed);
        (await AllUsersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_01_BootstrapFailsCleanlyWhenNoPasswordAndNoInteractiveInput()
    {
        using var factory = NewFactory(password: null);

        var (exit, output) = await RunAsync(factory, prompt: (_, _) => null);

        exit.Should().Be(BootstrapAdminCommand.Failed);
        output.Should().Contain("No admin password");
        (await AllUsersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_01_OutsideDevelopmentBootstrapNeedsAnExplicitSwitch()
    {
        using var factory = NewFactory(environment: "Production", allowNonDevelopment: false);

        var (exit, output) = await RunAsync(factory);

        exit.Should().Be(BootstrapAdminCommand.Failed);
        output.Should().Contain("Refusing").And.Contain("approval");
        (await AllUsersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_01_InDevelopmentBootstrapNeedsNoSwitch()
    {
        using var factory = NewFactory(environment: "Development", allowNonDevelopment: false);

        var (exit, _) = await RunAsync(factory);

        exit.Should().Be(BootstrapAdminCommand.Ok);
        (await AllUsersAsync()).Should().ContainSingle();
    }

    // The entry point itself (Program.cs): a password argument is refused before any host or database is touched.
    [Fact]
    public async Task BR_A_01_TheApiEntryPointRefusesAPasswordOnTheCommandLine_WithExitCode2()
    {
        var (exit, stdout, stderr) = await RunApiAsync(
            [BootstrapAdminCommand.Name, "--Bootstrap:AdminPassword=" + Secret], environment: "Production");

        exit.Should().Be(BootstrapAdminCommand.BadUsage);
        (stdout + stderr).Should().Contain("accepts no arguments").And.NotContain(Secret);
    }

    [Fact]
    public async Task BR_A_01_TheApiEntryPointRefusesToBootstrapInProductionWithoutTheSwitch_WithoutTouchingTheDatabase()
    {
        var (exit, stdout, stderr) = await RunApiAsync([BootstrapAdminCommand.Name], environment: "Production");

        exit.Should().Be(BootstrapAdminCommand.Failed);
        (stdout + stderr).Should().Contain("Refusing");
    }

    private static async Task<(int Exit, string StdOut, string StdErr)> RunApiAsync(string[] args, string environment)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location)!,
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout, await stderr);
    }
}
