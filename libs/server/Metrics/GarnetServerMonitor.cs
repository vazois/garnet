﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Garnet.common;
using Microsoft.Extensions.Logging;

namespace Garnet.server
{
    internal enum EventType : byte
    {
        COMMAND,
        STATS
    }

    internal sealed class GarnetServerMonitor
    {
        public readonly Dictionary<InfoMetricsType, bool>
            resetEventFlags = GarnetInfoMetrics.DefaultInfo.ToDictionary(x => x, y => false);

        public readonly Dictionary<LatencyMetricsType, bool>
            resetLatencyMetrics = GarnetLatencyMetrics.defaultLatencyTypes.ToDictionary(x => x, y => false);

        readonly StoreWrapper storeWrapper;
        readonly GarnetServerOptions opts;
        readonly IGarnetServer[] servers;
        readonly TimeSpan monitorSamplingFrequency;
        public long monitor_iterations;

        GarnetServerMetrics globalMetrics;
        readonly GarnetSessionMetrics accSessionMetrics;
        private ulong instant_input_net_bytes;
        private ulong instant_output_net_bytes;
        private ulong instant_commands_processed;
        private long startTimestamp;

        readonly CancellationTokenSource cts = new();
        readonly ManualResetEvent done = new(false);

        readonly ILogger logger;

        public GarnetServerMetrics GlobalMetrics => globalMetrics;

        SingleWriterMultiReaderLock rwLock = new();

        public GarnetServerMonitor(StoreWrapper storeWrapper, GarnetServerOptions opts, IGarnetServer[] servers, ILogger logger = null)
        {
            this.storeWrapper = storeWrapper;
            this.opts = opts;
            this.servers = servers;
            this.logger = logger;
            monitorSamplingFrequency = TimeSpan.FromSeconds(opts.MetricsSamplingFrequency);
            monitor_iterations = 0;

            instant_input_net_bytes = 0;
            instant_output_net_bytes = 0;
            instant_commands_processed = 0;
            startTimestamp = 0;
            globalMetrics = new(true, opts.LatencyMonitor, this);

            accSessionMetrics = new GarnetSessionMetrics();
        }

        public void Dispose()
        {
            cts.Cancel();
            done.WaitOne();
            cts.Dispose();
            done.Dispose();
            globalMetrics.Dispose();
        }

        public void Start()
        {
            Task.Run(() => MainMonitorTask(cts.Token));
        }

        public void AddMetricsHistorySessionDispose(GarnetSessionMetrics currSessionMetrics, GarnetLatencyMetricsSession currLatencyMetrics)
        {
            rwLock.WriteLock();
            try
            {
                if (currSessionMetrics != null) globalMetrics.historySessionMetrics.Add(currSessionMetrics);
                if (currLatencyMetrics != null) globalMetrics.globalLatencyMetrics.Merge(currLatencyMetrics);
                currLatencyMetrics?.Return();
            }
            finally { rwLock.WriteUnlock(); }
        }

        public string GetAllLocksets()
        {
            var result = "";
            foreach (var server in servers)
            {
                var sessions = ((GarnetServerBase)server).ActiveConsumers();
                foreach (var s in sessions)
                {
                    var session = (RespServerSession)s;
                    var lockset = session.txnManager.GetLockset();
                    if (lockset != "")
                        result += session.StoreSessionID + ": " + lockset + "\n";
                }
            }
            return result;
        }

        private void UpdateInstantaneousMetrics()
        {
            var currTimestamp = Stopwatch.GetTimestamp();
            var elapsedSec = monitorSamplingFrequency.TotalSeconds;
            globalMetrics.instantaneous_net_input_tpt = (globalMetrics.globalSessionMetrics.get_total_net_input_bytes() - instant_input_net_bytes) / (elapsedSec * GarnetServerMetrics.byteUnit);
            globalMetrics.instantaneous_net_output_tpt = (globalMetrics.globalSessionMetrics.get_total_net_output_bytes() - instant_output_net_bytes) / (elapsedSec * GarnetServerMetrics.byteUnit);
            globalMetrics.instantaneous_cmd_per_sec = (globalMetrics.globalSessionMetrics.get_total_commands_processed() - instant_commands_processed) / elapsedSec;

            globalMetrics.instantaneous_net_input_tpt = Math.Round(globalMetrics.instantaneous_net_input_tpt, 2);
            globalMetrics.instantaneous_net_output_tpt = Math.Round(globalMetrics.instantaneous_net_output_tpt, 2);
            globalMetrics.instantaneous_cmd_per_sec = Math.Round(globalMetrics.instantaneous_cmd_per_sec);

            startTimestamp = currTimestamp;
            instant_input_net_bytes = globalMetrics.globalSessionMetrics.get_total_net_input_bytes();
            instant_output_net_bytes = globalMetrics.globalSessionMetrics.get_total_net_output_bytes();
            instant_commands_processed = globalMetrics.globalSessionMetrics.get_total_commands_processed();
        }

        private void UpdateAllMetricsHistory()
        {
            //Reset session metrics accumulator
            accSessionMetrics.Reset();
            //Add session metrics history in accumulator
            accSessionMetrics.Add(globalMetrics.historySessionMetrics);
        }

        private void UpdateAllMetrics(IGarnetServer server)
        {
            //Accumulate metrics from all active sessions
            var sessions = ((GarnetServerBase)server).ActiveConsumers();
            foreach (var s in sessions)
            {
                var session = (RespServerSession)s;

                //Accumulate session metrics
                accSessionMetrics.Add(session.GetSessionMetrics);

                // Accumulate latency metrics if latency monitor is enabled
                if (opts.LatencyMonitor)
                {
                    rwLock.WriteLock();
                    try
                    {
                        // Add accumulated latency metrics for this iteration
                        globalMetrics.globalLatencyMetrics.Merge(session.GetLatencyMetrics());
                    }
                    finally
                    {
                        rwLock.WriteUnlock();
                    }
                }
            }

            // Reset global session metrics
            globalMetrics.globalSessionMetrics.Reset();
            // Add accumulated session metrics for this iteration
            globalMetrics.globalSessionMetrics.Add(accSessionMetrics);

        }

        private void ResetStats()
        {
            if (resetEventFlags[InfoMetricsType.STATS])
            {
                logger?.LogInformation("Resetting latency metrics for commands");
                globalMetrics.instantaneous_net_input_tpt = 0;
                globalMetrics.instantaneous_net_output_tpt = 0;
                globalMetrics.instantaneous_cmd_per_sec = 0;

                globalMetrics.total_connections_received = 0;
                globalMetrics.total_connections_disposed = 0;
                globalMetrics.globalSessionMetrics.Reset();
                globalMetrics.historySessionMetrics.Reset();

                foreach (var garnetServer in servers.Cast<GarnetServerBase>())
                {
                    var sessions = garnetServer.ActiveConsumers();
                    foreach (var s in sessions)
                    {
                        var session = (RespServerSession)s;
                        session.GetSessionMetrics.Reset();
                    }

                    garnetServer.ResetConnectionsReceived();
                    garnetServer.ResetConnectionsDiposed();
                }

                storeWrapper.clusterProvider?.ResetGossipStats();

                storeWrapper.ResetRevivificationStats();

                resetEventFlags[InfoMetricsType.STATS] = false;
            }
        }

        private void ResetLatencyMetrics()
        {
            if (opts.LatencyMonitor)
            {
                foreach (var eventType in resetLatencyMetrics.Keys)
                {
                    if (resetLatencyMetrics[eventType])
                    {
                        logger?.LogInformation("Resetting server-side stats {eventType}", eventType);

                        foreach (var server in servers)
                        {
                            var sessions = ((GarnetServerBase)server).ActiveConsumers();
                            foreach (var entry in sessions)
                                ((RespServerSession)entry).ResetLatencyMetrics(eventType);
                        }

                        rwLock.WriteLock();
                        try
                        {
                            globalMetrics.globalLatencyMetrics.Reset(eventType);
                        }
                        finally
                        {
                            rwLock.WriteUnlock();
                        }

                        resetLatencyMetrics[eventType] = false;
                    }
                }
            }
        }

        private void ResetLatencySessionMetrics()
        {
            if (opts.LatencyMonitor)
            {
                foreach (var server in servers)
                {
                    var sessions = ((GarnetServerBase)server).ActiveConsumers();
                    foreach (var entry in sessions)
                        ((RespServerSession)entry).ResetAllLatencyMetrics();
                }
            }
        }

        private async void MainMonitorTask(CancellationToken token)
        {
            startTimestamp = Stopwatch.GetTimestamp();
            try
            {
                while (true)
                {
                    await Task.Delay(monitorSamplingFrequency, token);

                    // Reset the session level latency metrics for the prior version, as we are
                    // about to make that the current version.
                    ResetLatencySessionMetrics();

                    monitor_iterations++;

                    foreach (var server in servers)
                    {
                        var garnetServer = ((GarnetServerBase)server);
                        globalMetrics.total_connections_received = garnetServer.TotalConnectionsReceived;
                        globalMetrics.total_connections_disposed = garnetServer.TotalConnectionsDisposed;
                        globalMetrics.total_connections_active = garnetServer.get_conn_active();

                        UpdateInstantaneousMetrics();
                        UpdateAllMetricsHistory();
                        UpdateAllMetrics(server);
                    }

                    //Reset & Cleanup
                    ResetStats();
                    ResetLatencyMetrics();
                }
            }
            catch (Exception ex)
            {
                logger?.LogCritical(ex, "MainMonitorTask exception");
            }
            finally
            {
                done.Set();
            }
        }
    }
}