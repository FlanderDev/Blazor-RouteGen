using System.Linq;
using Xunit;

namespace RouteGen.Generators.Tests;

public class PathsGeneratorTests
{
    [Fact]
    public void Generates_Const_For_Parameterless_Route_And_Method_For_Parameterized_Route()
    {
        var razorFiles = new[]
        {
            ("Home.razor", "@page \"/\"\n<h1>Home</h1>"),
            ("ModDetail.razor", "@page \"/mod/{id:int}\"\n<h1>Mod</h1>"),
            ("PublicProfile.razor", "@page \"/profile/{username}\"\n<h1>Profile</h1>"),
        };

        var result = GeneratorTestHarness.Run(
            "// no C# source needed for this test",
            includeAspNetCore: false,
            includeHttpClientFactory: false,
            additionalRazorFiles: razorFiles);

        var source = result.GetGeneratedSource("Paths.g.cs");
        Assert.NotNull(source);
        Assert.Contains("public const string Home = \"/\";", source);
        Assert.Contains("public static string ModDetail(int id)", source);
        Assert.Contains("=> $\"/mod/{id}\";", source);
        Assert.Contains("public static string PublicProfile(string username)", source);
        Assert.Contains("Uri.EscapeDataString(username)", source);
    }

    [Fact]
    public void GeneratedPathName_Override_Is_Honored()
    {
        var razorFiles = new[]
        {
            ("Index.razor", "@page \"/mods\"\n@attribute [GeneratedPathName(\"ModsIndex\")]\n<h1>Mods</h1>"),
        };

        var result = GeneratorTestHarness.Run(
            "// no C# source needed for this test",
            includeAspNetCore: false,
            includeHttpClientFactory: false,
            additionalRazorFiles: razorFiles);

        var source = result.GetGeneratedSource("Paths.g.cs");
        Assert.Contains("public const string ModsIndex = \"/mods\";", source);
    }

    [Fact]
    public void Ambiguous_Names_Report_RG0101()
    {
        var razorFiles = new[]
        {
            ("Feature/Index.razor", "@page \"/feature\"\n<h1>Feature</h1>"),
            ("Other/Index.razor", "@page \"/other\"\n<h1>Other</h1>"),
        };

        var result = GeneratorTestHarness.Run(
            "// no C# source needed for this test",
            includeAspNetCore: false,
            includeHttpClientFactory: false,
            additionalRazorFiles: razorFiles);

        Assert.Contains(result.Diagnostics, d => d.Id == "RG0101");
    }

    [Fact]
    public void No_Razor_Files_Means_No_Paths_Class_Is_Emitted()
    {
        var result = GeneratorTestHarness.Run(
            "// no C# source needed for this test",
            includeAspNetCore: false,
            includeHttpClientFactory: false);

        Assert.Null(result.GetGeneratedSource("Paths.g.cs"));
    }
}
