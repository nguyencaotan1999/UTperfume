namespace UTperfume.UnitTests;

public class AssemblyMarkerTests
{
    [Fact]
    public void Application_does_not_reference_aspnetcore_or_sqlserver_provider()
    {
        var references = typeof(UTperfume.Application.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        references.Should().NotContain(n => n!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        references.Should().NotContain(n => n!.Contains("SqlServer", StringComparison.Ordinal));
        references.Should().NotContain(n => n!.StartsWith("Azure.", StringComparison.Ordinal));
    }
}
