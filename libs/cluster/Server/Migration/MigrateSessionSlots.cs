// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using Garnet.server;
using Microsoft.Extensions.Logging;

namespace Garnet.cluster
{
    internal sealed unsafe partial class MigrateSession : IDisposable
    {
        /// <summary>
        /// Migrate slots main driver
        /// </summary>
        /// <returns></returns>
        public bool MigrateSlotsDriver()
        {
            logger?.LogTrace("Initializing [MainStore] Iterator");
            var bufferSize = 1 << clusterProvider.serverOptions.PageSizeBits();

            return clusterProvider.serverOptions.FastMigrate ? MigrateSlotsInline() : MigrateSlots();

            bool MigrateSlotsInline()
            {
                try
                {
                    var storeBeginAddress = clusterProvider.storeWrapper.store.Log.BeginAddress;
                    var storeTailAddress = clusterProvider.storeWrapper.store.Log.TailAddress;
                    logger?.LogTrace("[{method}] - Initiating [MainStore] scan [{BeginAddress} - {TailAddress}]", nameof(MigrateSlotsInline), storeBeginAddress, storeTailAddress);

                    var migrateTasks = new Task[clusterProvider.serverOptions.ParallelMigrateTasks];

                    var i = 0;
                    while (i < migrateTasks.Length)
                    {
                        var idx = i;
                        migrateTasks[idx] = Task.Run(() => IterateMainStoreTask(idx));
                        i++;
                    }

                    Task.WaitAll(migrateTasks, _cts.Token);

                    foreach (var migrateTask in migrateTasks)
                    {
                        if (migrateTask.IsFaulted || migrateTask.IsCanceled)
                            throw migrateTask.Exception;
                    }

                    // Delete keys that have migrated
                    Task.Run(() => ClusterManager.DeleteKeysInSlotsFromMainStore(localServerSession[0].BasicGarnetApi, _sslots));

                    Task<bool> IterateMainStoreTask(int taskId)
                    {
                        MigrationKeyIterationFunctions.MainStoreBloomFilter mainStoreBloomFilter = new(this, _sslots, taskId);
                        MigrationKeyIterationFunctions.MainStoreSendKeysInSlots mainStoreSendKeysInSlots = new(this, _sslots, taskId);
                        var range = (storeTailAddress - storeBeginAddress) / clusterProvider.storeWrapper.serverOptions.ParallelMigrateTasks;
                        var workerStartAddress = storeBeginAddress + (taskId * range);
                        var workerEndAddress = storeBeginAddress + ((taskId + 1) * range);

                        workerStartAddress = workerStartAddress - (2 * bufferSize) > 0 ? workerStartAddress - (2 * bufferSize) : 0;
                        workerEndAddress = workerEndAddress + (2 * bufferSize) < storeTailAddress ? workerEndAddress + (2 * bufferSize) : storeTailAddress;
                        var cursor = workerStartAddress;

                        // Build bloom filter
                        logger?.LogTrace("[{method}] - Start [MainStore] bloom filter build", nameof(MigrateSlotsInline));
                        _ = localServerSession[taskId].BasicGarnetApi.IterateMainStore(ref mainStoreBloomFilter, ref cursor, workerEndAddress);
                        logger?.LogTrace("[{method}] - Completed [MainStore] bloom filter build in range [{from}, {until}] until {cursor}, found {keyCount} keys",
                            nameof(MigrateSlotsInline), workerStartAddress, workerEndAddress, cursor, mainStoreSendKeysInSlots.keyCount);

                        // Iterate main store
                        cursor = workerStartAddress;
                        logger?.LogTrace("[{method}] - Start [MainStore] scan", nameof(MigrateSlotsInline));
                        _ = localServerSession[taskId].BasicGarnetApi.IterateMainStore(ref mainStoreSendKeysInSlots, ref cursor, workerEndAddress);
                        logger?.LogTrace("[{method}] - Completed [MainStore] scan in range [{from}, {until}] until {cursor}, found {keyCount} keys",
                            nameof(MigrateSlotsInline), workerStartAddress, workerEndAddress, cursor, mainStoreSendKeysInSlots.keyCount);

                        // Signal target transmission completed and log stats for main store after migration completes
                        if (!HandleMigrateTaskResponse(_gcs[taskId].CompleteMigrate(_sourceNodeId, _replaceOption, isMainStore: true)))
                            return Task.FromResult(false);

                        return Task.FromResult(true);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError("{method} {ex}", nameof(MigrateSlotsInline), ex);
                }
                return true;
            }

            bool MigrateSlots()
            {
                var storeTailAddress = clusterProvider.storeWrapper.store.Log.TailAddress;
                MigrationKeyIterationFunctions.MainStoreGetKeysInSlots mainStoreGetKeysInSlots = new(this, _sslots, bufferSize: bufferSize);

                try
                {
                    logger?.LogTrace("Initiating [MainStore] scan");
                    var mainStoreCursor = 0L;
                    while (true)
                    {
                        // Iterate main store
                        logger?.LogTrace("Start [MainStore] scan from {cursor}", mainStoreCursor);
                        _ = localServerSession[0].BasicGarnetApi.IterateMainStore(ref mainStoreGetKeysInSlots, ref mainStoreCursor, storeTailAddress);
                        logger?.LogTrace("Scanned [MainStore] {cursor}", mainStoreCursor);

                        // If did not acquire any keys stop scanning
                        if (_keys.IsNullOrEmpty())
                            break;

                        // Safely migrate keys to target node
                        logger?.LogTrace("Start processing batch");
                        if (!MigrateKeys(StoreType.Main, taskId: 0))
                        {
                            logger?.LogError("IOERR Migrate keys failed.");
                            Status = MigrateState.FAIL;
                            return false;
                        }
                        logger?.LogTrace("Complete processing batch");

                        mainStoreGetKeysInSlots.AdvanceIterator();
                        ClearKeys();
                    }

                    // Signal target transmission completed and log stats for main store after migration completes
                    if (!HandleMigrateTaskResponse(_gcs[0].CompleteMigrate(_sourceNodeId, _replaceOption, isMainStore: true)))
                        return false;
                }
                finally
                {
                    mainStoreGetKeysInSlots.Dispose();
                }

                if (!clusterProvider.serverOptions.DisableObjects)
                {
                    logger?.LogTrace("Initializing [ObjectStore] Iterator");
                    var objectStoreTailAddress = clusterProvider.storeWrapper.objectStore.Log.TailAddress;
                    var objectBufferSize = 1 << clusterProvider.serverOptions.ObjectStorePageSizeBits();
                    MigrationKeyIterationFunctions.ObjectStoreGetKeysInSlots objectStoreGetKeysInSlots = new(this, _sslots, bufferSize: objectBufferSize);
                    var objectStoreCursor = 0L;

                    try
                    {
                        logger?.LogTrace("Initiating [ObjectStore] scan");
                        while (true)
                        {
                            // Iterate object store
                            logger?.LogTrace("Start [ObjectStore] scan from {cursor}", objectStoreCursor);
                            _ = localServerSession[0].BasicGarnetApi.IterateObjectStore(ref objectStoreGetKeysInSlots, ref objectStoreCursor, objectStoreTailAddress);
                            logger?.LogTrace("Scanned [ObjectStore] until {cursor}", objectStoreCursor);

                            // If did not acquire any keys stop scanning
                            if (_keys.IsNullOrEmpty())
                                break;

                            // Safely migrate keys to target node
                            if (!MigrateKeys(StoreType.Object, taskId: 0))
                            {
                                logger?.LogError("IOERR Migrate keys failed.");
                                Status = MigrateState.FAIL;
                                return false;
                            }

                            objectStoreGetKeysInSlots.AdvanceIterator();
                            ClearKeys();
                        }

                        // Signal target transmission completed and log stats for object store after migration completes
                        if (!HandleMigrateTaskResponse(_gcs[0].CompleteMigrate(_sourceNodeId, _replaceOption, isMainStore: false)))
                            return false;
                    }
                    finally
                    {
                        objectStoreGetKeysInSlots.Dispose();
                    }
                }

                return true;
            }
        }
    }
}