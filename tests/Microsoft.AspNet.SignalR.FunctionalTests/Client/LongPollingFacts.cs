using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Transports;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.AspNet.SignalR.Tests
{
    public class LongPollingFacts
    {
        [Fact]
        public void PollingRequestHandlerDoesNotPollAfterClose()
        {
            var disconnectCts = new CancellationTokenSource();

            var mockConnection = new Mock<Client.IConnection>();
            mockConnection.SetupGet(c => c.JsonSerializer).Returns(JsonSerializer.CreateDefault());
            mockConnection.Setup(c => c.TotalTransportConnectTimeout).Returns(TimeSpan.FromSeconds(5));

            var pollTaskCompletionSource = new TaskCompletionSource<IResponse>();
            var pollingWh = new ManualResetEvent(false);

            var mockHttpClient = CreateFakeHttpClient(pollingWh, pollTaskCompletionSource.Task);
            var longPollingTransport = new LongPollingTransport(mockHttpClient.Object);

            Assert.True(
                longPollingTransport.Start(mockConnection.Object, string.Empty, disconnectCts.Token)
                    .Wait(TimeSpan.FromSeconds(15)));

            // wait for the first polling request
            Assert.True(pollingWh.WaitOne(TimeSpan.FromSeconds(2)));
            
            // stop polling loop
            disconnectCts.Cancel();

            // finish polling request
            pollTaskCompletionSource.SetResult(CreateResponse(string.Empty));

            // give it some time to make sure a new poll was not setup after verification
            Thread.Sleep(1000);

            mockHttpClient
                .Verify(c => c.Post(It.Is<string>(url => url.StartsWith("poll?")), It.IsAny<Action<Client.Http.IRequest>>(),
                    It.IsAny<IDictionary<string, string>>(), It.IsAny<bool>()), Times.Once());
        }

        private static Mock<IHttpClient> CreateFakeHttpClient(ManualResetEvent pollingWh, Task<IResponse> pollingTask)
        {
            var mockHttpClient = new Mock<IHttpClient>();
            mockHttpClient.Setup(m => m.Post(It.IsAny<string>(),
                It.IsAny<Action<Client.Http.IRequest>>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<bool>()))
                .Returns<string, Action<Client.Http.IRequest>, IDictionary<string, string>, bool>(
                    (url, request, postData, isLongRunning) =>
                    {
                        var responseMessage = string.Empty;
                        if (url.Contains("connect?"))
                        {
                            responseMessage = "{\"C\":\"d-C6243495-A,0|B,0|C,1|D,0\",\"S\":1,\"M\":[]}";
                        }
                        else if (url.Contains("poll?"))
                        {
                            pollingWh.Set();
                            return pollingTask;
                        }

                        return Task.FromResult(CreateResponse(responseMessage));
                    });

            mockHttpClient.Setup(
                m => m.Get(It.IsAny<string>(), It.IsAny<Action<Client.Http.IRequest>>(), It.IsAny<bool>()))
                .Returns<string, Action<Client.Http.IRequest>, bool>(
                    (url, request, isLongRunning) => Task.FromResult(CreateResponse("{ \"Response\" : \"started\"}")));

            return mockHttpClient;
        }

        private static IResponse CreateResponse(string contents)
        {
            var mockResponse = new Mock<IResponse>();
            mockResponse.Setup(r => r.GetStream())
                .Returns(new MemoryStream(Encoding.UTF8.GetBytes(contents)));

            return mockResponse.Object;
        }
    }
}
