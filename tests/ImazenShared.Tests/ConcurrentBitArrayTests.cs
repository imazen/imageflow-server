
using System;
using Imazen.Common.Instrumentation.Support;
using Xunit;

namespace Imazen.Common.Tests
{
    public class ConcurrentBitArrayTests
    {

        [Fact]
        public void SetBitTrue_WhenIndexIsNegative_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            ConcurrentBitArray bitArray = new(16_000_000);

            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => bitArray[-1] = true);
        }

        [Fact]
        public void SetBitTrue_WhenIndexIsEqualToBitCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            ConcurrentBitArray bitArray = new(8);

            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => bitArray[8] = true);
        }

        [Fact]
        public void SetBitTrue_WhenIndexIsGreaterThanBitCount_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            ConcurrentBitArray bitArray = new(8);

            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => bitArray[9] = true);
        }


        [Fact]
        public void TestSetBitTrue()
        {
            ConcurrentBitArray bitArray = new(64);
            bitArray[1] = true;
            bitArray[3] = true;
            Assert.Equal("000000000000000A", ToHex(bitArray.ToBytes()));
        }

        [Fact]
        public void TestMergeTrueBitsFrom()
        {
            ConcurrentBitArray bitArray1 = new(64);
            ConcurrentBitArray bitArray2 = new(64);
            bitArray1[1] = true;
            bitArray1[3] = true;
            bitArray2[2] = true;
            bitArray2[3] = true;
            bitArray1.MergeTrueBitsFrom(bitArray2);
            Assert.True(bitArray1[2]);
            Assert.True(bitArray1[3]);
        }

        [Fact]
        public void TestLoadFromBytesRoundtrip()
        {
            ConcurrentBitArray bitArray = new(64);
            bitArray.LoadFromBytes(new byte[] { 0, 14, 0, 0, 0, 0, 0, 0 });
            Assert.Equal(new byte[] { 0, 14, 0, 0, 0, 0, 0, 0 }, bitArray.ToBytes());
        }

        [Fact]
        public void TestToBytes()
        {
            ConcurrentBitArray bitArray = new(64);
            bitArray[1] = true;
            bitArray[3] = true;
            //Visual diff
            Assert.Equal("000000000000000A", ToHex(bitArray.ToBytes()));
            Assert.Equal("00000000-00000000-00000000-00000000-00000000-00000000-00000000-00001010", ToBinaryString(bitArray.ToBytes()));
        }

        // convert byte arrays to hex strings for visual diff
        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "");
        }
        private static string ToBinaryString(byte[] bytes)
        {
            string result = "";
            foreach (byte b in bytes)
            {
                result += Convert.ToString(b, 2).PadLeft(8, '0') + "-";
            }
            return result.TrimEnd('-');
        }

        [Fact]
        public void TestGetBitTrue()
        {
            ConcurrentBitArray bitArray = new(64);
            bitArray[1] = true;
            bitArray[3] = true;
            Assert.True(bitArray[1]);
            Assert.True(bitArray[3]);
            Assert.False(bitArray[2]);
        }


        [Fact]
        public void MergeTrueBitsFrom_ThrowsException_WhenBitArraySizesDoNotMatch2()
        {
            // Arrange
            ConcurrentBitArray bitArray1 = new(8);
            ConcurrentBitArray bitArray2 = new(16);

            // Act
            Action action = () => bitArray1.MergeTrueBitsFrom(bitArray2);

            // Assert
            Assert.Throws<ArgumentException>(action);
        }


        [Fact]
        public void TestSerialization()
        {
            // Arrange
            ConcurrentBitArray bitArray1 = new(16_000_000);
            ConcurrentBitArray bitArray2 = new(16_000_000);

            // Act
            bitArray1[1] = true;
            bitArray1[3] = true;
            bitArray2[2] = true;
            bitArray2[3] = true;

            byte[] serializedData1 = bitArray1.ToBytes();
            ConcurrentBitArray bitArray3 = new(16_000_000);
            bitArray3.LoadFromBytes(serializedData1);
            bitArray3.MergeTrueBitsFrom(bitArray2);

            // Assert
            byte[] serializedData3 = bitArray3.ToBytes();
            ConcurrentBitArray bitArray4 = new(16_000_000);
            bitArray4.LoadFromBytes(serializedData3);

            byte[] finalSerializedData = bitArray4.ToBytes();
            for (int i = 0; i < serializedData3.Length; i++)
            {
                Assert.Equal(serializedData3[i], finalSerializedData[i]);
            }
        }
    }
}