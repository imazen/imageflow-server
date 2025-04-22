using System.Collections.Generic;
using System.Diagnostics;


namespace Imazen.HybridCache.Tests
{
    public class BucketCounterTests
    {
        [Fact]
        public void TestGetHashMask()
        {
            Assert.Equal((uint)1, BucketCounter.GetHashMask(1)); 
            Assert.Equal((uint)3, BucketCounter.GetHashMask(2)); 
            Assert.Equal((uint)int.MaxValue, BucketCounter.GetHashMask(31));
            Assert.Equal((uint)int.MaxValue, BucketCounter.GetHashMask(32));
        }

        [Fact]
        public void TestGetHashes()
        {
            var bc = new BucketCounter(2);
            for (var i = 0; i <= 255; i++)
            {
                var hash = bc.GetHash(new[] {(byte)i});
                Assert.True(hash < bc.RowCount);
            }
        }
        
        [Fact]
        public void TestHashUniqueness()
        {
            var set = new HashSet<int>();
            // For SHA-1, we require 16 bits from the hash to uniquely distribute 256 values. 
            var bc = new BucketCounter(16);
            for (var i = 0; i <= 255; i++)
            {
                var hash = bc.GetHash(new[] {(byte)i});
                Assert.DoesNotContain(hash, set);
                set.Add(hash);
            }
        }
        
        [Fact]
        public void TestHashIncrements()
        {
            var bc = new BucketCounter(8);
            for (int i = 0; i <= 255; i++)
            {
                var hash = bc.GetHash(new[] {(byte)i});
                var baseline = bc.Get(hash);
                Assert.Equal(baseline, bc.Get(hash));
                bc.Increment(hash);
                Assert.Equal(baseline + 1, bc.Get(hash));
                bc.Increment(hash);
                Assert.Equal(baseline + 2, bc.Get(hash));
            }
        }

        [Fact]
        public void TestTransition()
        {
            var bc = new BucketCounter(21,true, 512);
            bc.Increment(1);
            bc.Increment(2);
            bc.Increment(2);
            bc.Increment(3);
            bc.Increment(3);
            bc.Increment(3);
            for (var i = 4; i < bc.MaxDictionarySize + 2; i++)
            {
                bc.Increment(i);
            }
            Assert.Equal(1, bc.Get(1));
            Assert.Equal(2, bc.Get(2));
            Assert.Equal(3, bc.Get(3));
            for (var i = 4; i < bc.MaxDictionarySize + 2; i++)
            {
                Assert.Equal(1, bc.Get(i));
            }
        }

        long BenchmarkIncrements(int bits, bool useStarterDictionary,  int uniques, int iterations)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var bc = new BucketCounter(bits, useStarterDictionary);
                for (var u = 0; u < uniques; u++)
                {
                    bc.Increment(u);
                    bc.Increment(u);
                    bc.Increment(u);
                }
            }
            sw.Stop();
            return sw.ElapsedTicks;
        }
        
        [Fact(Skip = "Benchmarking for investigation only")]
        private void BenchmarkIncrement()
        {
            //Tables-only is fastest by several orders of magnitude. 
            

            var dictOnly = BenchmarkIncrements(21, true, 510, 1000);
            var tableOnly = BenchmarkIncrements(21, false,510, 1000);
            var transitions = BenchmarkIncrements(21, true, 1020, 1000);

            Assert.True(tableOnly < dictOnly);
            Assert.True(transitions > dictOnly);
            Assert.True(transitions < dictOnly * 2);

        }
        
    }
}