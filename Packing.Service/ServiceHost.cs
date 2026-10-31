using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;
using Packing.Network;
using Packing.Service;

namespace Packing.Service {
    public static class ServiceHost {
        public static WebApplication Build(string[] launchArguments, Action<IServiceCollection> registerServices = null) {
            var hostBuilder = WebApplication.CreateBuilder(launchArguments);
            hostBuilder.WebHost.ConfigureKestrel(parallelOptions => parallelOptions.Limits.MaxRequestBodySize = 1024 * 1024);
            hostBuilder.Services.Configure<RouteHandlerOptions>(parallelOptions => parallelOptions.ThrowOnBadRequest = false);
            hostBuilder.Services.AddSingleton(new ServerState(hostBuilder.Configuration.GetValue<int?>("Compute:MaxThreads") ?? Environment.ProcessorCount));
            hostBuilder.Services.AddSingleton<BatchProcessor>();
            registerServices?.Invoke(hostBuilder.Services);
            var serviceApp = hostBuilder.Build();
            serviceApp.Use(async (requestContext, upcomingEntries) => {
                Stopwatch elapsedClock = Stopwatch.StartNew();
                try {
                    if (requestContext.Request.ContentLength > 1024 * 1024) {
                        requestContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                        return;
                    }

                    await upcomingEntries(requestContext);
                } finally {
                    serviceApp.Logger.LogInformation("HTTP {Method} {Path} {Status} {Milliseconds} ms Job={Job}", requestContext.Request.Method, requestContext.Request.Path, requestContext.Response.StatusCode, elapsedClock.Elapsed.TotalMilliseconds, requestContext.Request.Headers["X-Job-Id"].ToString());
                }
            });
            serviceApp.UseExceptionHandler(errorPipeline => errorPipeline.Run(async requestContext => {
                requestContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await Results.Problem("Внутренняя ошибка вычислительного сервиса.", statusCode: 500).ExecuteAsync(requestContext);
            }));
            serviceApp.MapGet("/health", (ServerState serviceCounters) => Results.Ok(serviceCounters.Snapshot()));
            serviceApp.MapGet("/metrics", (ServerState serviceCounters) => Results.Ok(serviceCounters.Snapshot()));
            serviceApp.MapPost("/api/batches", async (BatchRequest computeRequest, BatchProcessor packetProcessor, HttpContext requestContext, IHostApplicationLifetime serviceLifetime) => {
                using var searchStop = CancellationTokenSource.CreateLinkedTokenSource(requestContext.RequestAborted, serviceLifetime.ApplicationStopping);
                try {
                    BatchResponse httpReply = await packetProcessor.ExecuteAsync(computeRequest, searchStop.Token);
                    return Results.Ok(httpReply);
                } catch (ArgumentException caughtError) {
                    return Results.Problem(caughtError.Message, statusCode: 400);
                } catch (ServiceBusyException) {
                    requestContext.Response.Headers.RetryAfter = "1";
                    return Results.Problem("Сервис занят.", statusCode: 429);
                } catch (OperationCanceledException)when (searchStop.IsCancellationRequested) {
                    return Results.StatusCode(499);
                }
            });
            return serviceApp;
        }
    }
}
