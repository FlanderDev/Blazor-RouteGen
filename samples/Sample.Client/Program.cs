using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sample.Client;
using Sample.Shared;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The "App" name here matches [ApiRoute("api/mods", HttpClientName = "App")] on IModsApi.
builder.Services.AddHttpClient("App", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// The ONLY place IModsApi is wired up. Every Razor component just injects IModsApi and calls
// plain C# methods — no HttpClient, no URL strings, anywhere in UI code.
builder.Services.AddScoped<IModsApi, HttpModsApi>();

await builder.Build().RunAsync();
