// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Garnet.common;
using Garnet.server;
using Tsavorite.core;

namespace Garnet.cluster
{
    internal struct KeyData(ArgSlice slice, KeyMigrationStatus status)
    {
        public ArgSlice slice = slice;
        public KeyMigrationStatus status = status;
    }

    internal class MigratingKeysWorkingSet
    {
        readonly Dictionary<ArgSlice, KeyMigrationStatus> WorkingSet;

        readonly byte[] bitmap;
        readonly KeyData[] statusMap;
        readonly int maxKeys;
        readonly int maxKeysMask;
        public int KeyCount { get; private set; }
        readonly int maxProbing;

        public MigratingKeysWorkingSet()
        {
            WorkingSet = new Dictionary<ArgSlice, KeyMigrationStatus>(ArgSliceComparer.Instance);
            maxKeys = 1 << 22;
            maxKeysMask = maxKeys - 1;
            maxProbing = 512;
            KeyCount = 0;
            bitmap = GC.AllocateArray<byte>(maxKeys / 8, pinned: true);
            statusMap = GC.AllocateArray<KeyData>(maxKeys, pinned: true);
        }

        bool Occupied(int slot)
        {
            var byteOffset = slot >> 3;
            var bitOffset = slot & 7;
            return (bitmap[byteOffset] & (1UL << bitOffset)) > 0;
        }

        public IEnumerable<KeyValuePair<ArgSlice, KeyMigrationStatus>> GetKeys()
        {
            for (var i = 0; i < maxKeys; i++)
            {
                if (!Occupied(i))
                    continue;
                yield return new KeyValuePair<ArgSlice, KeyMigrationStatus>(statusMap[i].slice, statusMap[i].status);
            }
        }

        /// <summary>
        /// Check if migration working is empty or null
        /// </summary>
        /// <returns></returns>
        public bool IsNullOrEmpty() => KeyCount == 0;

        /// <summary>
        /// Hash key to bloomfilter
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public unsafe bool TryHash(ref SpanByte key)
        {
            var slot = (int)HashUtils.MurmurHash2x64A(key.ToPointer(), key.Length) & maxKeysMask;
            var byteOffset = slot >> 3;
            var bitOffset = slot & 7;
            bitmap[byteOffset] = (byte)(bitmap[byteOffset] | (1UL << bitOffset));
            return true;
        }

        /// <summary>
        /// Add key to migration working set with corresponding status
        /// </summary>
        /// <param name="key"></param>
        /// <param name="status"></param>
        public unsafe bool TryAdd(ref ArgSlice key, KeyMigrationStatus status)
        {
            var slot = (int)HashUtils.MurmurHash2x64A(key.SpanByte.ToPointer(), key.Length) & maxKeysMask;

            // Linear probing
            var probeCount = 0;
            while (Occupied(slot))
            {
                // Cannot add more keys
                if (probeCount++ > maxProbing) return false;

                slot = (slot + 1) & maxKeysMask;
            }

            var byteOffset = slot >> 3;
            var bitOffset = slot & 7;
            statusMap[slot] = new(key, status);
            bitmap[byteOffset] = (byte)(bitmap[byteOffset] | (1UL << bitOffset));
            KeyCount++;
            return true;
        }

        /// <summary>
        /// Try get status of corresponding key in working set
        /// </summary>
        /// <param name="key"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        public unsafe bool TryGetValue(ref ArgSlice key, out KeyMigrationStatus status)
        {
            var slot = (int)HashUtils.MurmurHash2x64A(key.SpanByte.ToPointer(), key.Length) & maxKeysMask;
            status = KeyMigrationStatus.QUEUED;

            // Linear probing
            var probeCount = 0;
            while (true)
            {
                // key does not exist if we reached the end of probing
                if (probeCount++ > maxProbing) return false;

                // key does not exist
                if (!Occupied(slot)) return false;

                // key found
                if (statusMap[slot].slice.Equals(key))
                    break;

                // move to next slot
                slot = (slot + 1) & maxKeysMask;
            }

            statusMap[slot].status = status;
            return true;
        }

        /// <summary>
        /// Update status of an existing key
        /// </summary>
        /// <param name="key"></param>
        /// <param name="status"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool UpdateStatus(ArgSlice key, KeyMigrationStatus status)
        {
            var slot = (int)HashUtils.MurmurHash2x64A(key.SpanByte.ToPointer(), key.Length) & maxKeysMask;

            // Linear probing
            var probeCount = 0;
            while (!statusMap[slot].slice.EqualsObject(key))
            {
                // key not updated because not found
                if (probeCount++ > maxProbing) return false;
                slot = (slot + 1) & maxKeysMask;
            }
            statusMap[slot].status = status;
            return false;
        }

        /// <summary>
        /// Clear keys from working set
        /// </summary>
        public void ClearKeys()
        {
            for (var i = 0; i < maxKeys / 8; i++)
            {
                bitmap[i] = 0;
                var offset = i << 3;
                statusMap[i] = default;
                statusMap[offset + 1] = default;
                statusMap[offset + 2] = default;
                statusMap[offset + 3] = default;
                statusMap[offset + 4] = default;
                statusMap[offset + 5] = default;
                statusMap[offset + 6] = default;
                statusMap[offset + 7] = default;
            }
            KeyCount = 0;
        }
    }
}