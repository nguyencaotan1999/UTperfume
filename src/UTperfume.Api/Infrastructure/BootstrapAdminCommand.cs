using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UTperfume.Infrastructure.Identity;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.Api.Infrastructure;

/// <summary>
/// <c>dotnet UTperfume.Api.dll bootstrap-admin</c>: creates the first <c>Catalog.Admin</c> user, only when no admin exists
/// (ADR-0010 section 6, architecture 7.1). The email and the password come from configuration (<c>Bootstrap:AdminEmail</c>,
/// <c>Bootstrap:AdminPassword</c>, supplied through user-secrets or environment variables) or, when run interactively
/// and a value is missing, from a prompt. The command accepts no other command-line argument, so a password can never
/// be passed on the command line (the host would otherwise read <c>--Bootstrap:AdminPassword=...</c> as configuration).
/// The password is never written to the log, to the console or to any file; only its Identity hash is stored.
/// Outside Development it also requires <c>Bootstrap:AllowNonDevelopment=true</c>, a deliberate switch; running it
/// against production needs the user's approval (CLAUDE.md safety rules).
/// </summary>
internal static partial class BootstrapAdminCommand
{
    public const string Name = "bootstrap-admin";
    public const string EmailKey = "Bootstrap:AdminEmail";
    public const string PasswordKey = "Bootstrap:AdminPassword";
    public const string DisplayNameKey = "Bootstrap:AdminDisplayName";
    public const string AllowNonDevelopmentKey = "Bootstrap:AllowNonDevelopment";
    private const string DefaultDisplayName = "Administrator";

    public const string UsageMessage =
        "bootstrap-admin accepts no arguments. Supply Bootstrap:AdminEmail and Bootstrap:AdminPassword " +
        "through user-secrets or environment variables (never on the command line), or run it interactively.";

    public const int Ok = 0;
    public const int Failed = 1;
    public const int BadUsage = 2;

    /// <summary>True when the first argument is the command name.</summary>
    public static bool IsRequested(string[] args) =>
        args is { Length: > 0 } && string.Equals(args[0], Name, StringComparison.Ordinal);

    /// <summary>The command takes no further arguments. Any other argument, in particular a password, is refused.</summary>
    public static bool HasOnlyTheCommandName(string[] args) => args is { Length: 1 };

    /// <summary>
    /// Runs the command. <paramref name="prompt"/> (label, mask the input) is called only when a value is missing from configuration and
    /// the process is interactive; it returns null when no input is available.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        IServiceProvider services,
        TextWriter output,
        Func<string, bool, string?> prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(prompt);

        if (!HasOnlyTheCommandName(args))
        {
            await output.WriteLineAsync(UsageMessage);
            return BadUsage;
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(BootstrapAdminCommand));

        if (!environment.IsDevelopment() && !(bool.TryParse(configuration[AllowNonDevelopmentKey], out var allowed) && allowed))
        {
            await output.WriteLineAsync(
                $"Refusing to run in the '{environment.EnvironmentName}' environment. Creating the first admin outside " +
                $"Development needs the user's approval; set {AllowNonDevelopmentKey}=true only after it is given.");
            return Failed;
        }

        var email = configuration[EmailKey]?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            email = prompt("Admin email: ", false)?.Trim();
        }

        var displayName = configuration[DisplayNameKey]?.Trim();
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = DefaultDisplayName;
        }

        if (string.IsNullOrEmpty(email))
        {
            await output.WriteLineAsync($"No admin email: set {EmailKey} or run interactively.");
            return Failed;
        }

        var password = configuration[PasswordKey];
        if (string.IsNullOrEmpty(password))
        {
            password = prompt("Admin password: ", true);
            if (!string.IsNullOrEmpty(password) && prompt("Repeat password: ", true) != password)
            {
                await output.WriteLineAsync("The passwords do not match.");
                return Failed;
            }
        }

        if (string.IsNullOrEmpty(password))
        {
            await output.WriteLineAsync($"No admin password: set {PasswordKey} or run interactively.");
            return Failed;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            await output.WriteLineAsync("The database has pending migrations. Apply them first (docs/dev-setup.md).");
            return Failed;
        }

        if ((await users.GetUsersInRoleAsync(AdminRoles.CatalogAdmin)).Count > 0)
        {
            await output.WriteLineAsync("An admin already exists. Nothing was changed.");
            return Ok;
        }

        var user = new AdminUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsActive = true,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };

        var create = await users.CreateAsync(user, password);
        if (!create.Succeeded)
        {
            // Identity error descriptions describe the rule that failed; they never contain the password.
            await output.WriteLineAsync("The admin could not be created:");
            foreach (var error in create.Errors)
            {
                await output.WriteLineAsync($"  - {error.Description}");
            }

            return Failed;
        }

        var role = await users.AddToRoleAsync(user, AdminRoles.CatalogAdmin);
        if (!role.Succeeded)
        {
            await users.DeleteAsync(user);
            await output.WriteLineAsync(
                $"The role '{AdminRoles.CatalogAdmin}' is missing. Apply the AddAdminIdentity migration first.");
            return Failed;
        }

        LogCreated(logger, user.Id);
        await output.WriteLineAsync($"Admin user {user.Id.ToString(CultureInfo.InvariantCulture)} created.");
        return Ok;
    }

    /// <summary>Reads one line from the console without echoing it when <paramref name="secret"/> is true. Null when input is redirected.</summary>
    public static string? ConsolePrompt(string label, bool secret)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        Console.Write(label);
        if (!secret)
        {
            return Console.ReadLine();
        }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    [LoggerMessage(EventId = 5110, Level = LogLevel.Information, Message = "Admin audit: bootstrap_admin AdminUser {UserId}")]
    private static partial void LogCreated(ILogger logger, int userId);
}
