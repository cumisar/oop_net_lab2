using System.Net;
using System.Net.Http.Json;
using System.Text;
using Packing.Core;
using Packing.Network;
using Xunit;

namespace Packing.Service.Tests {
    public sealed class ServiceScenarios : IClassFixture<ApiFactory>, IDisposable {
        private readonly HttpClient httpTransport;
        public ServiceScenarios(ApiFactory serviceFixture) {
            httpTransport = serviceFixture.CreateClient();
        }

        private static BatchRequest CreateTask() {
            return new BatchRequest {
                JobId = Guid.NewGuid(),
                Threads = 1,
                Figures = new[] {
                    new FigureDto {
                        Width = 3,
                        Height = 3
                    },
                    new FigureDto {
                        Width = 2,
                        Height = 2
                    }
                },
                Candidates = new[] {
                    new CandidateDto {
                        Index = 0,
                        Order = new[] {
                            0,
                            1
                        },
                        StripWidth = 3
                    }
                }
            };
        }

        [Fact]
        public async Task Service_computes_layout_and_updates_monitoring() {
            using var healthReply = await httpTransport.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthReply.StatusCode);
            var initialMetrics = await httpTransport.GetFromJsonAsync<ServiceMetrics>("/metrics");
            var samplePacket = CreateTask();
            using var httpReply = await httpTransport.PostAsJsonAsync("/api/batches", samplePacket);
            Assert.Equal(HttpStatusCode.OK, httpReply.StatusCode);
            var serviceReply = await httpReply.Content.ReadFromJsonAsync<BatchResponse>();
            Assert.Equal(samplePacket.JobId, serviceReply.JobId);
            var returnedLayout = Assert.Single(serviceReply.Solutions);
            var referenceLayout = BottomLeftDecoder.Decode(samplePacket.Validate(), new[] { 0, 1 }, 3);
            Assert.Equal(referenceLayout.Area, returnedLayout.Area);
            Assert.Equal(referenceLayout.Placements.Select(placedTile => placedTile.X), returnedLayout.X);
            Assert.Equal(referenceLayout.Placements.Select(placedTile => placedTile.Y), returnedLayout.Y);
            var finalMetrics = await httpTransport.GetFromJsonAsync<ServiceMetrics>("/metrics");
            Assert.Equal(initialMetrics.Completed + 1, finalMetrics.Completed);
            Assert.Equal(0, finalMetrics.Active);
        }

        [Fact]
        public async Task Invalid_permutation_is_rejected_then_valid_task_works() {
            var samplePacket = CreateTask();
            samplePacket.Candidates[0].Order = new[] {
                0,
                0
            };
            using var badReply = await httpTransport.PostAsJsonAsync("/api/batches", samplePacket);
            Assert.Equal(HttpStatusCode.BadRequest, badReply.StatusCode);
            using var goodReply = await httpTransport.PostAsJsonAsync("/api/batches", CreateTask());
            Assert.Equal(HttpStatusCode.OK, goodReply.StatusCode);
        }

        [Fact]
        public async Task Broken_json_does_not_stop_service() {
            using var badReply = await httpTransport.PostAsync("/api/batches", new StringContent("{", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, badReply.StatusCode);
            using var healthReply = await httpTransport.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthReply.StatusCode);
        }

        [Fact]
        public void Unavailable_service_uses_local_reserve() {
            using var workerDirectory = new ServerRegistry(endpointText => new HttpClient(new UnavailableHandler()));
            workerDirectory.Add("127.0.0.1:5080");
            using var layoutExecutor = new DistributedEvaluator(workerDirectory);
            var packingTask = CreateTask().Validate();
            PackingSolution restoredLayout = null;
            layoutExecutor.Evaluate(packingTask, new[] { new PackingCandidate(0, new[] { 0, 1 }, 3) }, 1, (entryNumber, packingLayout) => restoredLayout = packingLayout, CancellationToken.None);
            Assert.NotNull(restoredLayout);
            Assert.Equal(BottomLeftDecoder.Decode(packingTask, new[] { 0, 1 }, 3).Area, restoredLayout.Area);
            Assert.Equal(1, layoutExecutor.LocalCandidates);
            Assert.Equal(1, workerDirectory.Snapshot()[0].Failures);
        }

        private sealed class UnavailableHandler : HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage computeRequest, CancellationToken abortSignal) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
        }

        public void Dispose() {
            httpTransport.Dispose();
        }
    }
}
