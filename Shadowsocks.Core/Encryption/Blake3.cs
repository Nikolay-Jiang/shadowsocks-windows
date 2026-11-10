using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Shadowsocks.Encryption
{
    /// <summary>
    /// Portable BLAKE3 implementation.  Shadowsocks 2022 needs the hash mode
    /// (for SIP023 identity hashes) and the derive-key mode (for session and
    /// identity subkeys); keyed mode is not used.
    /// </summary>
    internal static class Blake3
    {
        private const int BlockLen = 64;
        private const int ChunkLen = 1024;
        private const int OutLen = 32;

        private const uint ChunkStart = 1 << 0;
        private const uint ChunkEnd = 1 << 1;
        private const uint Parent = 1 << 2;
        private const uint Root = 1 << 3;
        private const uint DeriveKeyContext = 1 << 5;
        private const uint DeriveKeyMaterial = 1 << 6;

        private static readonly uint[] Iv =
        {
            0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
            0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
        };

        private static readonly byte[] MsgPermutation = { 2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8 };

        public static byte[] Hash(ReadOnlySpan<byte> input)
        {
            return OneShot(input, 0, null);
        }

        public static byte[] DeriveKey(string context, ReadOnlySpan<byte> material)
        {
            byte[] contextKey = OneShot(Encoding.UTF8.GetBytes(context), DeriveKeyContext, null);
            uint[] contextWords = new uint[8];
            for (int i = 0; i < 8; i++)
            {
                contextWords[i] = BinaryPrimitives.ReadUInt32LittleEndian(contextKey.AsSpan(i * 4, 4));
            }
            return OneShot(material, DeriveKeyMaterial, contextWords);
        }

        private static byte[] OneShot(ReadOnlySpan<byte> input, uint flags, uint[] key)
        {
            var hasher = new Hasher(key ?? Iv, flags);
            hasher.Update(input);
            return hasher.Finalize(OutLen);
        }

        private sealed class Hasher
        {
            private readonly uint[] _key;
            private readonly uint _flags;
            private readonly List<uint[]> _cvStack = new List<uint[]>();
            private ChunkState _chunk;

            public Hasher(uint[] key, uint flags)
            {
                _key = key;
                _flags = flags;
                _chunk = new ChunkState(key, 0, flags);
            }

            public void Update(ReadOnlySpan<byte> input)
            {
                while (input.Length > 0)
                {
                    if (_chunk.Len == ChunkLen)
                    {
                        uint[] cv = _chunk.Output().ChainingValue();
                        AddChunkChainingValue(cv, _chunk.ChunkCounter + 1);
                        _chunk = new ChunkState(_key, _chunk.ChunkCounter + 1, _flags);
                    }

                    int want = ChunkLen - _chunk.Len;
                    int take = Math.Min(want, input.Length);
                    _chunk.Update(input.Slice(0, take));
                    input = input.Slice(take);
                }
            }

            private void AddChunkChainingValue(uint[] newCv, ulong totalChunks)
            {
                while ((totalChunks & 1) == 0)
                {
                    uint[] left = _cvStack[_cvStack.Count - 1];
                    _cvStack.RemoveAt(_cvStack.Count - 1);
                    newCv = ParentOutput(left, newCv, _key, _flags).ChainingValue();
                    totalChunks >>= 1;
                }
                _cvStack.Add(newCv);
            }

            public byte[] Finalize(int outLen)
            {
                Output output = _chunk.Output();
                for (int i = _cvStack.Count - 1; i >= 0; i--)
                {
                    output = ParentOutput(_cvStack[i], output.ChainingValue(), _key, _flags);
                }
                return output.RootBytes(outLen);
            }
        }

        private sealed class ChunkState
        {
            private uint[] _cv;
            private readonly ulong _chunkCounter;
            private readonly uint _flags;
            private readonly byte[] _block = new byte[BlockLen];
            private readonly uint[] _blockWords = new uint[16];
            private int _blockLen;
            private int _blocksCompressed;

            public ChunkState(uint[] key, ulong chunkCounter, uint flags)
            {
                _cv = (uint[])key.Clone();
                _chunkCounter = chunkCounter;
                _flags = flags;
            }

            public ulong ChunkCounter => _chunkCounter;

            public int Len => BlockLen * _blocksCompressed + _blockLen;

            public void Update(ReadOnlySpan<byte> input)
            {
                while (input.Length > 0)
                {
                    if (_blockLen == BlockLen)
                    {
                        uint startFlag = _blocksCompressed == 0 ? ChunkStart : 0;
                        uint[] output = Compress(_cv, WordsFromBytes(_block, BlockLen), _chunkCounter, BlockLen,
                            _flags | startFlag);
                        Array.Copy(output, _cv, 8);
                        _blocksCompressed++;
                        _blockLen = 0;
                    }

                    int want = BlockLen - _blockLen;
                    int take = Math.Min(want, input.Length);
                    input.Slice(0, take).CopyTo(_block.AsSpan(_blockLen));
                    _blockLen += take;
                    input = input.Slice(take);
                }
            }

            public Output Output()
            {
                uint startFlag = _blocksCompressed == 0 ? ChunkStart : 0;
                uint[] words = WordsFromBytes(_block, _blockLen);
                return new Output(_cv, words, _chunkCounter, (uint)_blockLen, _flags | startFlag | ChunkEnd);
            }
        }

        private sealed class Output
        {
            private readonly uint[] _inputCv;
            private readonly uint[] _blockWords;
            private readonly ulong _counter;
            private readonly uint _blockLen;
            private readonly uint _flags;

            public Output(uint[] inputCv, uint[] blockWords, ulong counter, uint blockLen, uint flags)
            {
                _inputCv = inputCv;
                _blockWords = blockWords;
                _counter = counter;
                _blockLen = blockLen;
                _flags = flags;
            }

            public uint[] ChainingValue()
            {
                uint[] output = Compress(_inputCv, _blockWords, _counter, _blockLen, _flags);
                uint[] cv = new uint[8];
                Array.Copy(output, cv, 8);
                return cv;
            }

            public byte[] RootBytes(int outLen)
            {
                byte[] result = new byte[outLen];
                int offset = 0;
                ulong counter = 0;
                Span<byte> word = stackalloc byte[4];
                while (offset < outLen)
                {
                    uint[] output = Compress(_inputCv, _blockWords, counter, _blockLen, _flags | Root);
                    for (int i = 0; i < 8 && offset < outLen; i++)
                    {
                        int take = Math.Min(4, outLen - offset);
                        if (take == 4)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), output[i]);
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(word, output[i]);
                            word.Slice(0, take).CopyTo(result.AsSpan(offset));
                        }
                        offset += take;
                    }
                    counter++;
                }
                return result;
            }
        }

        private static Output ParentOutput(uint[] left, uint[] right, uint[] key, uint flags)
        {
            uint[] block = new uint[16];
            Array.Copy(left, 0, block, 0, 8);
            Array.Copy(right, 0, block, 8, 8);
            return new Output(key, block, 0, BlockLen, flags | Parent);
        }

        private static uint[] WordsFromBytes(byte[] block, int length)
        {
            uint[] words = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                int offset = i * 4;
                if (offset >= length)
                {
                    words[i] = 0;
                }
                else if (offset + 4 <= length)
                {
                    words[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset, 4));
                }
                else
                {
                    uint value = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        if (offset + j < length)
                        {
                            value |= (uint)block[offset + j] << (8 * j);
                        }
                    }
                    words[i] = value;
                }
            }
            return words;
        }

        private static uint[] Compress(uint[] cv, uint[] block, ulong counter, uint blockLen, uint flags)
        {
            uint[] v = new uint[16];
            for (int i = 0; i < 8; i++)
            {
                v[i] = cv[i];
            }
            v[8] = Iv[0];
            v[9] = Iv[1];
            v[10] = Iv[2];
            v[11] = Iv[3];
            v[12] = (uint)counter;
            v[13] = (uint)(counter >> 32);
            v[14] = blockLen;
            v[15] = flags;

            uint[] m = (uint[])block.Clone();
            for (int round = 0; round < 7; round++)
            {
                G(v, 0, 4, 8, 12, m[0], m[1]);
                G(v, 1, 5, 9, 13, m[2], m[3]);
                G(v, 2, 6, 10, 14, m[4], m[5]);
                G(v, 3, 7, 11, 15, m[6], m[7]);
                G(v, 0, 5, 10, 15, m[8], m[9]);
                G(v, 1, 6, 11, 12, m[10], m[11]);
                G(v, 2, 7, 8, 13, m[12], m[13]);
                G(v, 3, 4, 9, 14, m[14], m[15]);

                if (round != 6)
                {
                    uint[] permuted = new uint[16];
                    for (int i = 0; i < 16; i++)
                    {
                        permuted[i] = m[MsgPermutation[i]];
                    }
                    m = permuted;
                }
            }

            uint[] result = new uint[16];
            for (int i = 0; i < 8; i++)
            {
                result[i] = v[i] ^ v[i + 8];
                result[i + 8] = v[i + 8] ^ cv[i];
            }
            return result;
        }

        private static void G(uint[] v, int a, int b, int c, int d, uint mx, uint my)
        {
            v[a] = v[a] + v[b] + mx;
            v[d] = RotateRight(v[d] ^ v[a], 16);
            v[c] = v[c] + v[d];
            v[b] = RotateRight(v[b] ^ v[c], 12);
            v[a] = v[a] + v[b] + my;
            v[d] = RotateRight(v[d] ^ v[a], 8);
            v[c] = v[c] + v[d];
            v[b] = RotateRight(v[b] ^ v[c], 7);
        }

        private static uint RotateRight(uint value, int bits)
        {
            return (value >> bits) | (value << (32 - bits));
        }
    }
}
