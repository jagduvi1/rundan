using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rundan.Client;
using Rundan.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same-origin API (the ASP.NET Core host serves this WASM app).
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<RundanApi>();

// Per-page resources (SignalR connection, geolocation watch, Leaflet map) are
// created with `new` inside the component that owns them, so they are disposed
// deterministically on navigation — Blazor does not dispose DI transients per component.

await builder.Build().RunAsync();
