using MudBlazor;
using MudBlazor.Services;
using zxadocsfe.Services;
using zxadocsui.Components;
using zxadocsui.State;
using zxadocui.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMudServices();
builder.Services.AddScoped<IHttpService, HttpService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<RequestContext>();
builder.Services.AddTransient<HttpCoreIntercetpor>();
builder.Services.AddScoped<IUserSession, UserSession>();

builder.Services.AddScoped<RequestContext>();
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
