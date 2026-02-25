using System.Runtime.InteropServices;

namespace Imazen.Common.Instrumentation.Support
{
    internal class ConcurrentBitArray
    {
        private readonly long[] _data;
        private readonly int _bitCount;

        public int BitCount => _bitCount;

        public int ByteCount => BitCount / 8;
        public ConcurrentBitArray(int bitCount)
        {
            // must be a multiple of 8
            if (bitCount % 8 != 0)
            {
                throw new ArgumentException("ConcurrentBitArray requires the bit count to be a multiple of 8");
            }
            //must be a platform in little endian mode
            if (!BitConverter.IsLittleEndian)
            {
                throw new ArgumentException("ConcurrentBitArray only supports little endian platforms");
            }
            _bitCount = bitCount;
            _data = new long[(bitCount + 63) / 64];
        }

   
        public void LoadFromBytes(byte[] bytes)
        {

            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (bytes.Length != ByteCount)
            {
                throw new ArgumentException($"Invalid input byte array size - must match the number of bytes in the ConcurrentBitArray {bytes.Length} != {ByteCount}");
            }
            Buffer.BlockCopy(bytes, 0, _data, 0, bytes.Length);
        }

        public void LoadFromSpan(Span<byte> bytes)
        {
            if (bytes.Length != ByteCount)
            {
                throw new ArgumentException($"Invalid input span size - must match the number of bytes in the ConcurrentBitArray {bytes.Length} != {ByteCount}");
            }
            if (bytes.Length > Buffer.ByteLength(_data)) throw new Exception($"Buffer is too small, {bytes.Length} > {Buffer.ByteLength(_data)}");
            bytes.CopyTo(MemoryMarshal.Cast<long, byte>((Span<long>)_data));
        }


        public byte[] ToBytes()
        {
            byte[] bytes = new byte[ByteCount];
            // Convert the long array to bytes in little endian, without using BlockCopy
            if (ByteCount > Buffer.ByteLength(_data)) throw new Exception($"Buffer is too small, {ByteCount} > {Buffer.ByteLength(_data)}");
            Buffer.BlockCopy(_data, 0, bytes, 0, bytes.Length);
            return bytes;
        }



        public void MergeTrueBitsFrom(ConcurrentBitArray other)
        {
            // must be the same size
            if (other._bitCount != _bitCount)
            {
                throw new ArgumentException("ConcurrentBitArray instances must be the same size");
            }
            // TODO, try Vector<long> for this
            for (int i = 0; i < _data.Length; i++)
            {
                long otherValue = other._data[i];
                long oldValue, newValue;
                if (otherValue == 0)
                {
                    continue; // nothing to do
                }
                do
                {
                    oldValue = _data[i];
                    newValue = oldValue | otherValue;
                } while (Interlocked.CompareExchange(ref _data[i], newValue, oldValue) != oldValue);
            }
        }

        
        public bool GetBit(int index)
        {
            if (index < 0 || index >= _bitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {_bitCount - 1}");
            }
            int arrayIndex = index / 64;
            int byteSubIndex = (index % 64) / 8;
            int bitSubIndex = index % 8;
            // Reverse for little endian so the raw bytes make sense.
            long bitMask = 1L << (56 - (byteSubIndex * 8) + bitSubIndex);
            return (_data[arrayIndex] & bitMask) != 0;
        }


        internal void SetBit(int index, bool value)
        {
            if (index < 0 || index >= _bitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {_bitCount - 1}");
            }
            int arrayIndex = index / 64;
            int byteSubIndex = (index % 64) / 8;
            int bitSubIndex = index % 8;
            // Reverse for little endian so the raw bytes make sense.
            long bitMask = 1L << (56 - (byteSubIndex * 8) + bitSubIndex);
            long oldValue, newValue;
            do
            {
                oldValue = _data[arrayIndex];
                newValue = value ? oldValue | bitMask : oldValue & ~bitMask;
            } while (Interlocked.CompareExchange(ref _data[arrayIndex], newValue, oldValue) != oldValue);


        }

        
        public bool this[int index]
        {
            get => GetBit(index);
            set => SetBit(index, value);
        }
    }
}
