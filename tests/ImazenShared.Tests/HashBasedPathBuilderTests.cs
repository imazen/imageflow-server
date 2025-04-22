using Imazen.Common.Extensibility.Support;
using Xunit;


namespace Imazen.HybridCache.Tests
{
    public class HashBasedPathBuilderTests
    {
        [Fact]
        public void BuildRelativePathForData()
        {
            var data1 = new byte[] {0, 1, 2, 3};
            var data2 = new byte[] {4, 3, 5, 3};
            Assert.StartsWith("1/00/054", 
                new HashBasedPathBuilder("",2, '/', "").GetDisplayPathForKeyBasis(data1));
            Assert.StartsWith("1/01/723", 
                new HashBasedPathBuilder("",2, '/', "").GetDisplayPathForKeyBasis(data2));

            Assert.StartsWith("2/00/054", 
                new HashBasedPathBuilder("",4, '/', "").GetDisplayPathForKeyBasis(data1));
            Assert.StartsWith("2/03/723", 
                new HashBasedPathBuilder("",4, '/', "").GetDisplayPathForKeyBasis(data2));
  
            Assert.StartsWith("13/10/d8/054", 
                new HashBasedPathBuilder("",8192, '/', "").GetDisplayPathForKeyBasis(data1));
            Assert.StartsWith("13/00/e3/723", 
                new HashBasedPathBuilder("",8192, '/', "").GetDisplayPathForKeyBasis(data2));

        }

        [Fact]
        public void TestSubfolderBits()
        {
            Assert.Equal(1, new HashBasedPathBuilder("",-1,'/',"").SubfolderBits);
            Assert.Equal(1, new HashBasedPathBuilder("",0,'/',"").SubfolderBits);
            Assert.Equal(1, new HashBasedPathBuilder("",2,'/',"").SubfolderBits);
            Assert.Equal(8, new HashBasedPathBuilder("",129,'/',"").SubfolderBits);
            Assert.Equal(8, new HashBasedPathBuilder("",256,'/',"").SubfolderBits);
            Assert.Equal(9, new HashBasedPathBuilder("",257,'/',"").SubfolderBits);
            Assert.Equal(12, new HashBasedPathBuilder("",4096,'/',"").SubfolderBits);
            Assert.Equal(13, new HashBasedPathBuilder("",8192,'/',"").SubfolderBits);
        }

        [Fact]
        public void TestGetTrailingBits()
        {
            var data = new byte[] {1, 2, 3};
            var result1 = HashBasedPathBuilder.GetTrailingBits(data, 1);
            Assert.Equal(1, result1[0]);
            Assert.Single(result1);
            var result2 = HashBasedPathBuilder.GetTrailingBits(data, 2);
            Assert.Equal(3, result2[0]);
            Assert.Single(result2);
            var result3 = HashBasedPathBuilder.GetTrailingBits(data, 8);
            Assert.Equal(3, result3[0]);
            Assert.Single(result3);
            var result4 = HashBasedPathBuilder.GetTrailingBits(data, 9);
            Assert.Equal(3, result4[1]);
            Assert.Equal(0, result4[0]);
            Assert.Equal(2, result4.Length);
            
            var result5 = HashBasedPathBuilder.GetTrailingBits(data, 10);
            Assert.Equal(3, result5[1]);
            Assert.Equal(2, result5[0]);
            Assert.Equal(2, result5.Length);
            
            var result6 = HashBasedPathBuilder.GetTrailingBits(data, 17);
            Assert.Equal(3, result6[2]);
            Assert.Equal(2, result6[1]);
            Assert.Equal(1, result6[0]);
            Assert.Equal(3, result6.Length);
            
        }
    }
}