using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Packing.Service.Tests {
    public sealed class ApiFactory : WebApplicationFactory<global::Program> {
        protected override void ConfigureWebHost(IWebHostBuilder hostBuilder) {
            hostBuilder.UseEnvironment("Testing");
            hostBuilder.ConfigureLogging(logOptions => logOptions.ClearProviders());
        }
    }
}
