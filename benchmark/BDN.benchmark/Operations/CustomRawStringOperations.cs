// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Embedded.server;
using Garnet.common;

namespace BDN.benchmark.Operations
{


    /// <summary>
    /// Benchmark for RawStringOperations
    /// </summary>
    [MemoryDiagnoser]
    public unsafe class CustomRawStringOperations : OperationsBase
    {
        readonly BenchUtils benchUtils = new();

        List<Request> setOps = [];
        List<Request> getOps = [];
        List<int> offsets;
        int currReq = 0;
        int ksize = 8;
        int vsize = 8;
        int skipParseBytes;

        public void CreateGetSet(int keySize = 8, int valueSize = 8, int batchSize = 100)
        {
            var kvPairs = new (byte[], byte[])[batchSize];
            for (var i = 0; i < batchSize; i++)
            {
                kvPairs[i] = (new byte[keySize], new byte[valueSize]);

                benchUtils.RandomBytes(ref kvPairs[i].Item1);
                benchUtils.RandomBytes(ref kvPairs[i].Item2);
            }

            var setByteCount = ("*3\r\n$3\r\nSET\r\n"u8.Length + 1 + NumUtils.CountDigits(keySize) + 2 + keySize + 2 + 1 + NumUtils.CountDigits(valueSize) + 2 + valueSize + 2);
            var getByteCount = ("*2\r\n$3\r\nGET\r\n"u8.Length + 1 + NumUtils.CountDigits(keySize) + 2 + keySize + 2);
            foreach (var kv in kvPairs)
            {
                Request setReq = default;
                setReq.buffer = GC.AllocateArray<byte>(setByteCount, pinned: true);
                setReq.bufferPtr = (byte*)Unsafe.AsPointer(ref setReq.buffer[0]);
                var curr = setReq.bufferPtr;
                var end = setReq.bufferPtr + setReq.buffer.Length;
                _ = RespWriteUtils.TryWriteArrayLength(3, ref curr, end);
                _ = RespWriteUtils.TryWriteBulkString("SET"u8, ref curr, end);
                _ = RespWriteUtils.TryWriteBulkString(kv.Item1, ref curr, end);
                _ = RespWriteUtils.TryWriteBulkString(kv.Item2, ref curr, end);
                setOps.Add(setReq);

                Request getReq = default;
                getReq.buffer = GC.AllocateArray<byte>(getByteCount, pinned: true);
                getReq.bufferPtr = (byte*)Unsafe.AsPointer(ref getReq.buffer[0]);
                curr = getReq.bufferPtr;
                end = getReq.bufferPtr + getReq.buffer.Length;
                _ = RespWriteUtils.TryWriteArrayLength(2, ref curr, end);
                _ = RespWriteUtils.TryWriteBulkString("GET"u8, ref curr, end);
                _ = RespWriteUtils.TryWriteBulkString(kv.Item1, ref curr, end);
                getOps.Add(getReq);
            }

            var rnd = new Random();
            var numbers = Enumerable.Range(0, getOps.Count).ToList();

            offsets = Enumerable.Range(0, numbers.Count)
                   .Select(_ => rnd.Next(0, numbers.Count))
                   .ToList();

            skipParseBytes = "*2\r\n$3\r\nGET\r\n"u8.Length + 1 + NumUtils.CountDigits(keySize) + 2; // Skip to start of key
        }

        public override void GlobalSetup()
        {
            base.GlobalSetup();

            CreateGetSet(ksize, vsize);
            foreach (var setOp in setOps)
                SlowConsumeMessage(setOp.buffer.AsSpan());

            server.StoreWrapper.FlushAndEvict();
        }

        [Benchmark]
        public void GetFoundRandom()
        {
            Send(getOps[offsets[currReq++ % getOps.Count]]);
        }

        [Benchmark]
        public void GetFoundNoParsing()
        {
            var req = getOps[offsets[currReq++ % getOps.Count]];

            var curr = req.bufferPtr + skipParseBytes; // Skip "*2\r\n$3\r\nGET\r\n"
            session.DirectGET(curr, ksize);
        }
    }
}