using Sample.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IModsService, InMemoryModsService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
