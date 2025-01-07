// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Garnet.common;
using Garnet.networking;

namespace Garnet.client
{
    /// <summary>
    /// Mono-threaded remote client session for Garnet (a session makes a single network connection, and 
    /// expects mono-threaded client access, i.e., no concurrent invocations of API by client)
    /// </summary>
    public sealed unsafe partial class GarnetClientSession : IServerHook, IMessageConsumer
    {
        static ReadOnlySpan<byte> GOSSIP => "GOSSIP"u8;
        static ReadOnlySpan<byte> WITHMEET => "WITHMEET"u8;

        /// <summary>
        /// Send gossip message to corresponding node
        /// </summary>
        /// <param name="data"></param>
        /// <param name="meet"></param>
        /// <returns></returns>
        public Task<MemoryResult<byte>> ExecuteGossip(Memory<byte> data, bool meet = false)
        {
            var tcs = new TaskCompletionSource<MemoryResult<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcsByteArrayQueue.Enqueue(tcs);
            byte* curr = offset;
            int arraySize = meet ? 4 : 3;

            while (!RespWriteUtils.WriteArrayLength(arraySize, ref curr, end))
            {
                Flush();
                curr = offset;
            }
            offset = curr;

            //1
            while (!RespWriteUtils.WriteDirect(CLUSTER, ref curr, end))
            {
                Flush();
                curr = offset;
            }
            offset = curr;

            //2
            while (!RespWriteUtils.WriteBulkString(GOSSIP, ref curr, end))
            {
                Flush();
                curr = offset;
            }
            offset = curr;

            if (meet)
            {
                //3
                while (!RespWriteUtils.WriteBulkString(WITHMEET, ref curr, end))
                {
                    Flush();
                    curr = offset;
                }
                offset = curr;
            }

            //4
            while (!RespWriteUtils.WriteBulkString(data.Span, ref curr, end))
            {
                Flush();
                curr = offset;
            }
            offset = curr;

            Flush();
            Interlocked.Increment(ref numCommands);
            return tcs.Task;
        }
    }
}