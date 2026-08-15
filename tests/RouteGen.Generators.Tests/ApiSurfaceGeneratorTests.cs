using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace RouteGen.Generators.Tests;

public class ApiSurfaceGeneratorTests
{
    private const string ModsApiInterface = """
using System.Threading;
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    public class ModListResult { }
    public class Mod { }
    public class ModUploadDto { }

    [ApiRoute("api/mods", HttpClientName = "App")]
    public partial interface IModsApi
    {
        [Get]
        Task<ModListResult> GetMods([Query] int page = 1, [Query] int pageSize = 18, [Query] string? search = null);

        [Get("{id:int}")]
        Task<Mod> GetMod(int id);

        [Post("upload")]
        Task<Mod> Upload([Body] ModUploadDto dto);

        [Delete("{id:int}")]
        Task Delete(int id, CancellationToken ct = default);
    }
}
""";

    [Fact]
    public void ControllerBase_Is_Generated_When_AspNetCore_Referenced()
    {
        var result = GeneratorTestHarness.Run(ModsApiInterface, includeAspNetCore: true, includeHttpClientFactory: false);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var source = result.GetGeneratedSource("ControllerBase.g.cs");
        Assert.NotNull(source);
        Assert.Contains("[Route(\"api/mods\")]", source);
        Assert.Contains("[ApiController]", source);
        Assert.Contains("public abstract class ModsApiControllerBase : ControllerBase", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpGet(\"{id:int}\")]", source);
        Assert.Contains("[HttpPost(\"upload\")]", source);
        Assert.Contains("[HttpDelete(\"{id:int}\")]", source);
        Assert.Contains("[FromQuery] int page = 1", source);
        Assert.Contains("[FromBody] App.Shared.ModUploadDto dto", source);

        // No client should be emitted for a project that doesn't reference IHttpClientFactory.
        Assert.Null(result.GetGeneratedSource("Client.g.cs"));
    }

    [Fact]
    public void Client_Is_Generated_When_HttpClientFactory_Referenced()
    {
        var result = GeneratorTestHarness.Run(ModsApiInterface, includeAspNetCore: false, includeHttpClientFactory: true);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var source = result.GetGeneratedSource("Client.g.cs");
        Assert.NotNull(source);
        Assert.Contains("public sealed class HttpModsApi(IHttpClientFactory httpClientFactory) : IModsApi", source);
        Assert.Contains("httpClientFactory.CreateClient(\"App\")", source);
        Assert.Contains("throw new ApiException(HttpMethod.Get, url, response.StatusCode, body);", source);

        Assert.Null(result.GetGeneratedSource("ControllerBase.g.cs"));
    }

    [Fact]
    public void Both_Are_Generated_When_Both_Referenced()
    {
        var result = GeneratorTestHarness.Run(ModsApiInterface, includeAspNetCore: true, includeHttpClientFactory: true);

        Assert.NotNull(result.GetGeneratedSource("ControllerBase.g.cs"));
        Assert.NotNull(result.GetGeneratedSource("Client.g.cs"));
    }

    [Fact]
    public void Cross_Project_Boundary_Interface_Declared_In_Referenced_Assembly_Is_Discovered()
    {
        // Simulate the real Shared/Server topology: compile the interface into its own
        // assembly first (as "App.Shared" would be), then reference that assembly from a
        // second compilation ("App.Server") that declares no source of its own. This is the
        // scenario the whole package exists for, so it gets an explicit test rather than being
        // assumed to work because syntax-based discovery "usually" does.
        var sharedResult = GeneratorTestHarness.Run(ModsApiInterface, includeAspNetCore: false, includeHttpClientFactory: false);
        var sharedAssemblyRef = Microsoft.CodeAnalysis.MetadataReference.CreateFromStream(
            EmitToStream(sharedResult.OutputCompilation));

        var serverCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "App.Server",
            new[] { Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("// no source declares IModsApi here", path: "Empty.cs") },
            new[]
            {
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("Microsoft.AspNetCore.Mvc.Core").Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("Microsoft.AspNetCore.Mvc.Abstractions").Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("Microsoft.AspNetCore.Authorization").Location),
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("Microsoft.AspNetCore.Http.Abstractions").Location),
                sharedAssemblyRef,
            },
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        var driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(new ApiSurfaceGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(serverCompilation, out _, out _);
        var runResult = driver.GetRunResult();

        var generated = runResult.Results.SelectMany(r => r.GeneratedSources).FirstOrDefault(s => s.HintName.Contains("ControllerBase"));
        Assert.Contains("ModsApiControllerBase", generated.SourceText?.ToString() ?? "");
    }

    [Fact]
    public void Route_Collision_Reports_RG0001()
    {
        const string source = """
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    [ApiRoute("api/things")]
    public interface IThingsApi
    {
        [Get("x")]
        Task<string> A();

        [Get("x")]
        Task<string> B();
    }
}
""";
        var result = GeneratorTestHarness.Run(source, includeAspNetCore: false, includeHttpClientFactory: false);
        Assert.Contains(result.Diagnostics, d => d.Id == "RG0001");
    }

    [Fact]
    public void Body_On_Get_Reports_RG0002()
    {
        const string source = """
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    public class Dto { }

    [ApiRoute("api/things")]
    public interface IThingsApi
    {
        [Get("x")]
        Task<string> A([Body] Dto dto);
    }
}
""";
        var result = GeneratorTestHarness.Run(source, includeAspNetCore: false, includeHttpClientFactory: false);
        Assert.Contains(result.Diagnostics, d => d.Id == "RG0002");
    }

    [Fact]
    public void Unmatched_Route_Token_Reports_RG0003()
    {
        const string source = """
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    [ApiRoute("api/things")]
    public interface IThingsApi
    {
        [Get("{id:int}")]
        Task<string> A();
    }
}
""";
        var result = GeneratorTestHarness.Run(source, includeAspNetCore: false, includeHttpClientFactory: false);
        Assert.Contains(result.Diagnostics, d => d.Id == "RG0003");
    }

    [Fact]
    public void Unmatched_Parameter_Reports_RG0004()
    {
        const string source = """
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    [ApiRoute("api/things")]
    public interface IThingsApi
    {
        [Get]
        Task<string> A(int unused);
    }
}
""";
        var result = GeneratorTestHarness.Run(source, includeAspNetCore: false, includeHttpClientFactory: false);
        Assert.Contains(result.Diagnostics, d => d.Id == "RG0004");
    }

    [Fact]
    public void Multiple_Body_Parameters_Reports_RG0005()
    {
        const string source = """
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    public class Dto { }

    [ApiRoute("api/things")]
    public interface IThingsApi
    {
        [Post]
        Task<string> A([Body] Dto one, [Body] Dto two);
    }
}
""";
        var result = GeneratorTestHarness.Run(source, includeAspNetCore: false, includeHttpClientFactory: false);
        Assert.Contains(result.Diagnostics, d => d.Id == "RG0005");
    }

    [Fact]
    public void Complex_Type_In_Query_Reports_RG0006_As_Warning_But_Still_Emits()
    {
        const string source = """
using System.Threading.Tasks;
using RouteGen.Abstractions;

namespace App.Shared
{
    public class Filter { }

    [ApiRoute("api/things")]
    public interface IThingsApi
    {
        [Get]
        Task<string> A([Query] Filter filter);
    }
}
""";
        var result = GeneratorTestHarness.Run(source, includeAspNetCore: false, includeHttpClientFactory: true);
        var diag = Assert.Single(result.Diagnostics, d => d.Id == "RG0006");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, diag.Severity);

        // A warning must not block emission (only errors do).
        Assert.NotNull(result.GetGeneratedSource("Client.g.cs"));
    }

    private static System.IO.MemoryStream EmitToStream(Microsoft.CodeAnalysis.Compilation compilation)
    {
        var stream = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));
        stream.Position = 0;
        return stream;
    }
}
