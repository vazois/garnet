// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Garnet.client;
using Garnet.common;
using Microsoft.Extensions.Logging;

namespace Garnet.cluster
{
    internal sealed class GarnetServerNode
    {
        readonly ClusterProvider clusterProvider;
        readonly GarnetClientSession gcs;

        long gossipSend;
        long gossipRecv;
        CancellationTokenSource cts = new();
        CancellationTokenSource internalCts = new();
        volatile int initialized = 0;
        readonly ILogger logger = null;
        int disposeCount = 0;

        /// <summary>
        /// Last transmitted configuration
        /// </summary>
        ClusterConfig lastConfig = null;

        /// <summary>
        /// Lock used for GarnetClientSession to avoid parallel gossip issues by main gossip thread and meet request
        /// </summary>
        SingleWriterMultiReaderLock gcsLock;

        /// <summary>
        /// Outstanding gossip task if any
        /// </summary>
        Task gossipTask = null;

        /// <summary>
        /// Timestamp of last gossipSend for this connection
        /// </summary>
        public long GossipSend => gossipSend;

        /// <summary>
        /// GarnetClient connection
        /// </summary>
        public GarnetClient Client => null;

        /// <summary>
        /// NodeId of remote node
        /// </summary>
        public string NodeId;

        /// <summary>
        /// Address of remote node
        /// </summary>
        public string Address;

        /// <summary>
        /// Port of remote node
        /// </summary>
        public int Port;

        /// <summary>
        /// GarnetServerNode constructor
        /// </summary>
        /// <param name="clusterProvider"></param>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="tlsOptions"></param>
        /// <param name="logger"></param>
        public GarnetServerNode(ClusterProvider clusterProvider, string address, int port, SslClientAuthenticationOptions tlsOptions, ILogger logger = null)
        {
            this.clusterProvider = clusterProvider;
            this.Address = address;
            this.Port = port;

            this.gcs = new(
                address,
                port,
                networkBufferSettings: new NetworkBufferSettings(1 << 17),
                networkPool: null,
                tlsOptions,
                authUsername: clusterProvider.clusterManager.clusterProvider.ClusterUsername,
                authPassword: clusterProvider.clusterManager.clusterProvider.ClusterPassword,
                logger: logger);

            this.initialized = 0;
            this.logger = logger;
            this.gossipRecv = 0;
            this.gossipSend = 0;
            ResetCts();
        }

        /// <summary>
        /// Initialize connection and cancellation tokens.
        /// Initialization is performed only once
        /// </summary>
        public void Initialize()
        {
            try
            {
                gcsLock.WriteLock();
                // Ensure initialize executes only once
                if (Interlocked.CompareExchange(ref initialized, 1, 0) != 0) return;

                cts = CancellationTokenSource.CreateLinkedTokenSource(clusterProvider.clusterManager.ctsGossip.Token, internalCts.Token);
                gcs.Reconnect(clusterProvider.clusterManager.gossipDelay.Milliseconds, cts.Token);
            }
            finally
            {
                gcsLock.WriteUnlock();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Increment(ref disposeCount) != 1)
                logger?.LogTrace("GarnetServerNode.Dispose called multiple times");
            try
            {
                cts?.Cancel();
                cts?.Dispose();
                internalCts?.Cancel();
                internalCts?.Dispose();
                gcs?.Dispose();
            }
            catch { }
        }

        void UpdateGossipSend() => this.gossipSend = DateTimeOffset.UtcNow.Ticks;
        void UpdateGossipRecv() => this.gossipRecv = DateTimeOffset.UtcNow.Ticks;

        void ResetCts()
        {
            bool internalCtsDisposed = false;
            internalCts.Cancel();
            if (!internalCts.TryReset())
            {
                internalCts.Dispose();
                internalCts = new();
                internalCtsDisposed = true;
            }

            if (internalCtsDisposed || !cts.TryReset())
            {
                cts.Cancel();
                cts.Dispose();
                cts = CancellationTokenSource.CreateLinkedTokenSource(clusterProvider.clusterManager.ctsGossip.Token, internalCts.Token);
            }
            gossipTask = null;
        }

        /// <summary>
        /// Keep track of updated config per connection. Useful when gossip sampling so as to ensure updates are propagated
        /// </summary>
        /// <returns></returns>
        byte[] GetMostRecentConfig()
        {
            var conf = clusterProvider.clusterManager.CurrentConfig;
            byte[] byteArray;
            if (conf != lastConfig)
            {
                if (clusterProvider.replicationManager != null) conf.LazyUpdateLocalReplicationOffset(clusterProvider.replicationManager.ReplicationOffset);
                byteArray = conf.ToByteArray();
                lastConfig = conf;
            }
            else
            {
                byteArray = [];
            }
            return byteArray;
        }

        /// <summary>
        /// Schedule a Gossip task for provided serialized configuration
        /// </summary>
        /// <param name="configByteArray"></param>
        /// <returns></returns>
        private Task Gossip(byte[] configByteArray)
        {
            try
            {
                gcsLock.WriteLock();
                return gcs.ExecuteGossip(configByteArray).ContinueWith(t =>
                {
                    try
                    {
                        var resp = t.Result;
                        if (resp.Length > 0)
                        {
                            clusterProvider.clusterManager.gossipStats.UpdateGossipBytesRecv(resp.Length);
                            var returnedConfigArray = resp.Span.ToArray();
                            var other = ClusterConfig.FromByteArray(returnedConfigArray);
                            var current = clusterProvider.clusterManager.CurrentConfig;
                            // Check if gossip is from a node that is known and trusted before merging
                            if (current.IsKnown(other.LocalNodeId))
                                clusterProvider.clusterManager.TryMerge(ClusterConfig.FromByteArray(returnedConfigArray));
                            else
                                logger?.LogWarning("Received gossip from unknown node: {node-id}", other.LocalNodeId);
                        }
                        resp.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogCritical(ex, "GOSSIP faulted processing response");
                    }
                    finally
                    {
                        gcsLock.WriteUnlock();
                    }
                }, TaskContinuationOptions.OnlyOnRanToCompletion).WaitAsync(clusterProvider.clusterManager.gossipDelay, cts.Token);
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "GOSSIP faulted at send");
                gcsLock.WriteUnlock();
                return null;
            }
        }

        /// <summary>
        /// Issue gossip meet with meet to force receiving node to trust an untrusted node
        /// </summary>
        /// <param name="configByteArray"></param>
        /// <returns></returns>
        public async Task<MemoryResult<byte>> TryMeet(byte[] configByteArray)
        {
            try
            {
                gcsLock.WriteLock();
                UpdateGossipSend();
                return await gcs.ExecuteGossip(configByteArray, meet: true).WaitAsync(clusterProvider.clusterManager.clusterTimeout, cts.Token);
            }
            finally
            {
                gcsLock.WriteUnlock();
            }
        }

        /// <summary>
        /// Send gossip message or process response and send again.
        /// </summary>
        /// <returns></returns>
        public bool TryGossip()
        {
            var task = gossipTask;
            // If first time we are sending gossip make sure to send latest version
            if (task == null)
            {
                // Issue first time gossip
                var configArray = clusterProvider.clusterManager.CurrentConfig.ToByteArray();
                gossipTask = Gossip(configArray);
                // Handle fault at send
                if (gossipTask == null) return false;

                UpdateGossipSend();
                clusterProvider.clusterManager.gossipStats.gossip_full_send++;
                // Track bytes send
                clusterProvider.clusterManager.gossipStats.UpdateGossipBytesSend(configArray.Length);
                return true;
            }
            else if (task.Status == TaskStatus.RanToCompletion)
            {
                var configByteArray = GetMostRecentConfig();
                UpdateGossipRecv();

                // Issue new gossip that can be either zero packet size or an updated configuration
                gossipTask = Gossip(configByteArray);
                UpdateGossipSend();

                // Track number of full vs empty (ping) sends
                if (configByteArray.Length > 0)
                    clusterProvider.clusterManager.gossipStats.gossip_full_send++;
                else
                    clusterProvider.clusterManager.gossipStats.gossip_empty_send++;

                // Track bytes send
                clusterProvider.clusterManager.gossipStats.UpdateGossipBytesSend(configByteArray.Length);
                return true;
            }
            logger?.LogWarning(task.Exception, "GOSSIP round faulted");
            ResetCts();
            gossipTask = null;
            return false;
        }

        public ConnectionInfo GetConnectionInfo()
        {
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var last_io_seconds = gossipRecv == 0 ? 0 : (int)TimeSpan.FromTicks(nowTicks - gossipSend).TotalSeconds;

            return new ConnectionInfo()
            {
                ping = gossipSend,
                pong = gossipRecv,
                connected = gcs.IsConnected,
                lastIO = last_io_seconds,
            };
        }
    }
}