using System.Net;
using System.Net.Http;
using Imazen.Common.Licensing;
using Moq;
using Moq.Protected;

namespace Imazen.Tests.Licensing
{
    internal static class MockHttpHelpers
    {
        public static Mock<HttpMessageHandler> MockRemoteLicense(LicenseManagerSingleton mgr, HttpStatusCode code, string value,
                                                   Action<HttpRequestMessage, CancellationToken>? callback)
        {
            var handler = new Mock<HttpMessageHandler>();
            var method = handler.Protected()
                                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                                    ItExpr.IsAny<CancellationToken>())
                                .Returns(Task.Run(() => new HttpResponseMessage(code)
                                {
                                    Content = new StringContent(value, System.Text.Encoding.UTF8)
                                }));

            if (callback != null)
            {
                method.Callback(callback);
            }

            method.Verifiable("SendAsync must be called");

            mgr.SetHttpMessageHandler(handler.Object, true);
            return handler;
        }

        public static Mock<HttpMessageHandler> MockRemoteLicenseException(LicenseManagerSingleton mgr, WebExceptionStatus status)
        {
            var ex = new HttpRequestException("Mock failure", new WebException("Mock failure", status));
            var handler = new Mock<HttpMessageHandler>();
            var method = handler.Protected()
                                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                                    ItExpr.IsAny<CancellationToken>())
                                .ThrowsAsync(ex);

            method.Verifiable("SendAsync must be called");

            mgr.SetHttpMessageHandler(handler.Object, true);
            return handler;
        }
    }
}
;