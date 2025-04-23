using Xunit;

namespace Imageflow.Server.LegacyBlob
{
    public class TestBlob
    {
        [Fact]
        public void TestLoadsClass()
        {
            var blob = new RemoteReaderBlob(new HttpResponseMessage());
            Assert.NotNull(blob);
        }
    }
}