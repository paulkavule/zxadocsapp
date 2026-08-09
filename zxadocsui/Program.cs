using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor;
using MudBlazor.Services;
using zxadocsfe.Services;
using zxadocslib.Helpers;
using zxadocsui.Components;
using zxadocsui.Srevices;
using zxadocsui.State;
using zxadocui.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);
EnvHelper.LoadVariables(".env");
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 10000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});
builder.Services.AddScoped<IHttpService, HttpService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<UserSession>();
builder.Services.AddScoped<IUserSession>(sp => sp.GetRequiredService<UserSession>());
builder.Services.AddScoped<ITokenProvider>(sp => sp.GetRequiredService<UserSession>());

builder.Services.AddScoped<SideDialogService>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<RequestsContext>();

// Legal Templates client services (ZD-16 / FE-01) — scoped per user/circuit.
builder.Services.AddScoped<ITemplateClientService, TemplateClientService>();
builder.Services.AddScoped<IDraftClientService, DraftClientService>();
// Concrete-then-forward, like UserSession above: the same instance must be reachable as both
// IPermissionClientService and IScopedUserState, or the reset would clear a different object.
builder.Services.AddScoped<PermissionClientService>();
builder.Services.AddScoped<IPermissionClientService>(sp => sp.GetRequiredService<PermissionClientService>());
builder.Services.AddScoped<IListOptionClientService, ListOptionClientService>();
builder.Services.AddScoped<IActivityClientService, ActivityClientService>();
builder.Services.AddScoped<IAuditClientService, AuditClientService>();
builder.Services.AddScoped<IUserRoleClientService, UserRoleClientService>();
builder.Services.AddScoped<DraftHandoffState>();

// Per-circuit caches that belong to ONE signed-in user. Each forwards to the SAME scoped
// instance registered above, so UserSession.ResetUserState() clears the live objects rather
// than fresh copies. Register any new user-specific cache here or it will leak across a
// logout/login on the same circuit (see IScopedUserState); ScopedUserStateRegistrationTests
// fails the build if one is missed.
builder.Services.AddScoped<IScopedUserState>(sp => sp.GetRequiredService<AppState>());
builder.Services.AddScoped<IScopedUserState>(sp => sp.GetRequiredService<RequestsContext>());
builder.Services.AddScoped<IScopedUserState>(sp => sp.GetRequiredService<DraftHandoffState>());
builder.Services.AddScoped<IScopedUserState>(sp => sp.GetRequiredService<PermissionClientService>());

builder.Services.AddScoped<HttpCoreIntercetpor>();

// Behind nginx the container speaks plain HTTP; without this UseHttpsRedirection bounces
// every request to a TLS port that is not open. The proxy's bridge IP is unpredictable and
// the app port is never published, so nginx is the only possible client.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                       | ForwardedHeaders.XForwardedProto
                       | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// Api__BaseUrl comes from compose env_file; the dev default keeps `dotnet run` working.
builder.Services.AddHttpClient("Api", conf =>
{
    conf.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7028/");
}).AddHttpMessageHandler<HttpCoreIntercetpor>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Editor images reach .NET as base64 over a JS interop call (ZD-84). SignalR's default
    // 32 KB cap silently drops the circuit for anything larger — about a 24 KB image — so it
    // is raised to cover the configured image limit plus base64's 4/3 overhead. The real
    // ceiling stays the server-side Templates:MaxImageFileMb check, which rejects with 413.
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 8 * 1024 * 1024);

var app = builder.Build();

// Must precede UseHttpsRedirection and UseHsts. No-op without the headers.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// Stored HTML references images by a relative URL, so the browser asks this origin for them.
// Forward to the API: the signed query string authorises the read, so no bearer is needed.
app.MapGet("/api/templates/images", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    var upstream = await factory.CreateClient("Api").GetAsync(
        $"api/templates/images{ctx.Request.QueryString}",
        HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);

    ctx.Response.StatusCode = (int)upstream.StatusCode;
    if (!upstream.IsSuccessStatusCode) return;

    ctx.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "image/png";
    await upstream.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
