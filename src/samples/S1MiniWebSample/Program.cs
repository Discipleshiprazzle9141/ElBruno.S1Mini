using ElBruno.S1Mini;
using S1MiniWebSample.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddTranscriptNormalizer(options =>
{
    var localPath = Environment.GetEnvironmentVariable("S1MINI_MODEL_PATH");
    if (!string.IsNullOrWhiteSpace(localPath))
    {
        options.ModelPath = localPath;
        options.EnsureModelDownloaded = false;
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
