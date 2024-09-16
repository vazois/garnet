// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using Garnet.common;
using Tsavorite.core;

namespace Garnet.server
{
    sealed partial class StorageSession : IDisposable
    {
        public static bool Expired(ref SpanByte value) => value.MetadataSize > 0 && value.ExtraMetadata < DateTimeOffset.UtcNow.Ticks;

        public static bool Expired(ref IGarnetObject value) => value.Expiration != 0 && value.Expiration < DateTimeOffset.UtcNow.Ticks;

        internal static class ClusterKeyIterationFunctions
        {
            internal class MainStoreCountKeys : IScanIteratorFunctions<SpanByte, SpanByte>
            {
                internal int keyCount;
                readonly int slot;

                internal MainStoreCountKeys(int slot) => this.slot = slot;

                public bool SingleReader(ref SpanByte key, ref SpanByte value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                {
                    cursorRecordResult = CursorRecordResult.Accept; // default; not used here
                    if (HashSlotUtils.HashSlot(ref key) == slot && !Expired(ref value))
                        keyCount++;
                    return true;
                }
                public bool ConcurrentReader(ref SpanByte key, ref SpanByte value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                    => SingleReader(ref key, ref value, recordMetadata, numberOfRecords, out cursorRecordResult);
                public bool OnStart(long beginAddress, long endAddress) => true;
                public void OnStop(bool completed, long numberOfRecords) { }
                public void OnException(Exception exception, long numberOfRecords) { }
            }

            internal class ObjectStoreCountKeys : IScanIteratorFunctions<byte[], IGarnetObject>
            {
                internal int keyCount;
                readonly int slot;

                internal ObjectStoreCountKeys(int slot) => this.slot = slot;

                public bool SingleReader(ref byte[] key, ref IGarnetObject value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                {
                    cursorRecordResult = CursorRecordResult.Accept; // default; not used here , out CursorRecordResult cursorRecordResult
                    if (HashSlotUtils.HashSlot(key) == slot && !Expired(ref value))
                        keyCount++;
                    return true;
                }
                public bool ConcurrentReader(ref byte[] key, ref IGarnetObject value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                    => SingleReader(ref key, ref value, recordMetadata, numberOfRecords, out cursorRecordResult);
                public bool OnStart(long beginAddress, long endAddress) => true;
                public void OnStop(bool completed, long numberOfRecords) { }
                public void OnException(Exception exception, long numberOfRecords) { }
            }

            internal class MainStoreGetKeysInSlot : IScanIteratorFunctions<SpanByte, SpanByte>
            {
                readonly List<byte[]> keys;
                readonly int slot, maxKeyCount;

                internal MainStoreGetKeysInSlot(List<byte[]> keys, int slot, int maxKeyCount)
                {
                    this.keys = keys;
                    this.slot = slot;
                    this.maxKeyCount = maxKeyCount;
                }

                public bool SingleReader(ref SpanByte key, ref SpanByte value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                {
                    cursorRecordResult = CursorRecordResult.Accept; // default; not used here, out CursorRecordResult cursorRecordResult
                    if (HashSlotUtils.HashSlot(ref key) == slot && !Expired(ref value))
                        keys.Add(key.ToByteArray());
                    return keys.Count < maxKeyCount;
                }
                public bool ConcurrentReader(ref SpanByte key, ref SpanByte value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                    => SingleReader(ref key, ref value, recordMetadata, numberOfRecords, out cursorRecordResult);
                public bool OnStart(long beginAddress, long endAddress) => true;
                public void OnStop(bool completed, long numberOfRecords) { }
                public void OnException(Exception exception, long numberOfRecords) { }
            }

            internal class ObjectStoreGetKeysInSlot : IScanIteratorFunctions<byte[], IGarnetObject>
            {
                readonly List<byte[]> keys;
                readonly int slot;

                internal ObjectStoreGetKeysInSlot(List<byte[]> keys, int slot)
                {
                    this.keys = keys;
                    this.slot = slot;
                }

                public bool SingleReader(ref byte[] key, ref IGarnetObject value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                {
                    cursorRecordResult = CursorRecordResult.Accept; // default; not used here
                    if (HashSlotUtils.HashSlot(key) == slot && !Expired(ref value))
                        keys.Add(key);
                    return true;
                }
                public bool ConcurrentReader(ref byte[] key, ref IGarnetObject value, RecordMetadata recordMetadata, long numberOfRecords, out CursorRecordResult cursorRecordResult)
                    => SingleReader(ref key, ref value, recordMetadata, numberOfRecords, out cursorRecordResult);
                public bool OnStart(long beginAddress, long endAddress) => true;
                public void OnStop(bool completed, long numberOfRecords) { }
                public void OnException(Exception exception, long numberOfRecords) { }
            }
        }

        internal int CountKeysInSlot(int slot)
        {
            ClusterKeyIterationFunctions.MainStoreCountKeys iterFuncs = new(slot);
            var cursor = 0L;
            _ = basicContext.Session.ScanCursor(ref cursor, long.MaxValue, iterFuncs);
            var totalCount = iterFuncs.keyCount;

            if (objectStoreBasicContext.Session != null)
            {
                ClusterKeyIterationFunctions.ObjectStoreCountKeys objectStoreIterFuncs = new(slot);
                cursor = 0L;
                _ = objectStoreBasicContext.Session.ScanCursor(ref cursor, long.MaxValue, objectStoreIterFuncs);
                totalCount += objectStoreIterFuncs.keyCount;
            }

            return totalCount;
        }

        public List<byte[]> GetKeysInSlot(int slot, int keyCount)
        {
            List<byte[]> keys = [];
            ClusterKeyIterationFunctions.MainStoreGetKeysInSlot mainIterFuncs = new(keys, slot, keyCount);
            var cursor = 0L;
            _ = basicContext.Session.ScanCursor(ref cursor, long.MaxValue, mainIterFuncs);

            if (objectStoreBasicContext.Session != null)
            {
                ClusterKeyIterationFunctions.ObjectStoreGetKeysInSlot objectIterFuncs = new(keys, slot);
                cursor = 0L;
                _ = objectStoreBasicContext.Session.ScanCursor(ref cursor, long.MaxValue, objectIterFuncs);
            }
            return keys;
        }
    }
}