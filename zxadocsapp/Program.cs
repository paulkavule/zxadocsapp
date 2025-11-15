using zxadocsapp.Components;
using zxadocsapp.Infrastructure.Http;
using zxadocsfe.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IHttpService, HttpService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddTransient<HttpCoreIntercetpor>();


builder.Services.AddHttpClient("Api", conf =>
{
    conf.BaseAddress = new Uri("https://localhost:7028/");
})
.AddHttpMessageHandler<HttpCoreIntercetpor>();
// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
