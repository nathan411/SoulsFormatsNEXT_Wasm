using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Runtime.InteropServices.JavaScript;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
await builder.Build().RunAsync();
