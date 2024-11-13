﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Garnet.common;
using Microsoft.Extensions.Logging;

namespace Garnet.cluster
{
    internal sealed partial class ReplicationManager : IDisposable
    {
        void ThrottlePrimary()
        {
            while (replayIterator != null && storeWrapper.appendOnlyFile.TailAddress - ReplicationOffset > storeWrapper.serverOptions.ReplicationOffsetMaxLag)
            {
                replicaReplayTaskCts.Token.ThrowIfCancellationRequested();
                Thread.Yield();
            }
        }

        /// <summary>
        /// Apply primary AOF records.
        /// </summary>
        /// <param name="record"></param>
        /// <param name="recordLength"></param>
        /// <param name="previousAddress"></param>
        /// <param name="currentAddress"></param>
        /// <param name="nextAddress"></param>
        public unsafe void ProcessPrimaryStream(byte* record, int recordLength, long previousAddress, long currentAddress, long nextAddress)
        {
            // logger?.LogInformation("Processing {recordLength} bytes; previousAddress {previousAddress}, currentAddress {currentAddress}, nextAddress {nextAddress}, current AOF tail {tail}", recordLength, previousAddress, currentAddress, nextAddress, storeWrapper.appendOnlyFile.TailAddress);
            var currentConfig = clusterProvider.clusterManager.CurrentConfig;
            try
            {
                if (clusterProvider.replicationManager.Recovering)
                {
                    logger?.LogWarning("Replica is recovering cannot sync AOF");
                    throw new GarnetException("Replica is recovering cannot sync AOF", LogLevel.Warning, clientResponse: false);
                }

                if (currentConfig.LocalNodeRole != NodeRole.REPLICA)
                {
                    logger?.LogWarning("This node {nodeId} is not a replica", currentConfig.LocalNodeId);
                    throw new GarnetException($"This node {currentConfig.LocalNodeId} is not a replica", LogLevel.Warning, clientResponse: false);
                }

                if (clusterProvider.serverOptions.MainMemoryReplication)
                {
                    // If the incoming AOF chunk fits in the space between previousAddress and currentAddress (ReplicationOffset),
                    // an enqueue will result in an offset mismatch. So, we have to first reset the AOF to point to currentAddress.
                    if (currentAddress > previousAddress)
                    {
                        if (
                            (currentAddress % (1 << storeWrapper.appendOnlyFile.UnsafeGetLogPageSizeBits()) != 0) || // the skip was to a non-page-boundary
                            (currentAddress >= previousAddress + recordLength) // the skip will not be auto-handled by the AOF enqueue
                            )
                        {
                            logger?.LogWarning("MainMemoryReplication: Skipping from {ReplicaReplicationOffset} to {currentAddress}", ReplicationOffset, currentAddress);
                            storeWrapper.appendOnlyFile.SafeInitialize(currentAddress, currentAddress);
                            ReplicationOffset = currentAddress;
                        }
                    }
                }

                // Enqueue to AOF
                _ = clusterProvider.storeWrapper.appendOnlyFile?.UnsafeEnqueueRaw(new Span<byte>(record, recordLength), noCommit: clusterProvider.serverOptions.EnableFastCommit);

                // Throttle to give the opportunity to the background replay task to catch up
                ThrottlePrimary();

                // If background task has not been initialized
                // initialize it here and start background replay task
                if (replayIterator == null)
                {
                    replayIterator = clusterProvider.storeWrapper.appendOnlyFile.ScanSingle(
                        previousAddress,
                        long.MaxValue,
                        scanUncommitted: true,
                        recover: false,
                        logger: logger);

                    Task.Run(ReplicaReplayTask);
                }
                //Consume(record, recordLength, currentAddress, nextAddress, isProtected: false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "An exception occurred at ReplicationManager.ProcessPrimaryStream");
                ResetReplayIterator();
                throw new GarnetException(ex.Message, ex, LogLevel.Warning, clientResponse: false);
            }
        }
    }
}