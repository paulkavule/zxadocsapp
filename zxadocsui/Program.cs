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
builder.Services.AddScoped<IPermissionClientService, PermissionClientService>();
builder.Services.AddScoped<zxadocsui.State.DraftHandoffState>();

builder.Services.AddScoped<HttpCoreIntercetpor>();

builder.Services.AddHttpClient("Api", conf =>
{
    conf.BaseAddress = new Uri("https://localhost:7028/");
}).AddHttpMessageHandler<HttpCoreIntercetpor>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
