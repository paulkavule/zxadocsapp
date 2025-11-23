using MudBlazor;
using MudBlazor.Services;
using zxadocsapp.Client.Pages;
using zxadocsapp.Components;
using zxadocsapp.Infrastructure.Http;
using zxadocsapp.State;
using zxadocsapp.States;
using zxadocsfe.Services;

var builder = WebApplication.CreateBuilder(args);

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
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddScoped<IHttpService, HttpService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<RequestContext>();
builder.Services.AddTransient<HttpCoreIntercetpor>();
builder.Services.AddScoped<IUserSession, UserSession>();

builder.Services.AddHttpClient("Api", conf =>
{
    conf.BaseAddress = new Uri("https://localhost:7028/");
}).AddHttpMessageHandler<HttpCoreIntercetpor>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(zxadocsapp.Client._Imports).Assembly);

app.Run();
