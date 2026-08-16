using SampleApp.Server.Services;
using SampleApp.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IModsService, InMemoryModsService>();
builder.Services.AddAuthorization();

var app = builder.Build();

// The sample keeps auth wiring minimal (no real identity provider configured) since the point
// of this sample is demonstrating generated routing/binding, not an auth pipeline.
app.UseAuthorization();

app.MapControllers();

// Serve the Blazor WebAssembly client for a true end-to-end "Hosted" experience.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
