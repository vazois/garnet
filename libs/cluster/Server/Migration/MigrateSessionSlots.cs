// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
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
            var storeTailAddress = clusterProvider.storeWrapper.store.Log.TailAddress;
            var bufferSize = 1 << clusterProvider.serverOptions.PageSizeBits();
            MigrationKeyIterationFunctions.MainStoreGetKeysInSlots mainStoreGetKeysInSlots = new(this, _sslots, bufferSize: bufferSize);

            try
            {
                logger?.LogTrace("Initiating [MainStore] scan");
                var mainStoreCursor = 0L;
                while (true)
                {
                    // Iterate main store
                    logger?.LogTrace("Start [MainStore] scan from {cursor}", mainStoreCursor);
                    _ = localServerSession.BasicGarnetApi.IterateMainStore(ref mainStoreGetKeysInSlots, ref mainStoreCursor, storeTailAddress);
                    logger?.LogTrace("Scanned [MainStore] {cursor}", mainStoreCursor);

                    // If did not acquire any keys stop scanning
                    if (_keys.IsNullOrEmpty())
                        break;

                    // Safely migrate keys to target node
                    logger?.LogTrace("Start processing batch");
                    if (!MigrateKeys(StoreType.Main))
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
                if (!HandleMigrateTaskResponse(_gcs.CompleteMigrate(_sourceNodeId, _replaceOption, isMainStore: true)))
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
                        _ = localServerSession.BasicGarnetApi.IterateObjectStore(ref objectStoreGetKeysInSlots, ref objectStoreCursor, objectStoreTailAddress);
                        logger?.LogTrace("Scanned [ObjectStore] until {cursor}", objectStoreCursor);

                        // If did not acquire any keys stop scanning
                        if (_keys.IsNullOrEmpty())
                            break;

                        // Safely migrate keys to target node
                        if (!MigrateKeys(StoreType.Object))
                        {
                            logger?.LogError("IOERR Migrate keys failed.");
                            Status = MigrateState.FAIL;
                            return false;
                        }

                        objectStoreGetKeysInSlots.AdvanceIterator();
                        ClearKeys();
                    }

                    // Signal target transmission completed and log stats for object store after migration completes
                    if (!HandleMigrateTaskResponse(_gcs.CompleteMigrate(_sourceNodeId, _replaceOption, isMainStore: false)))
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