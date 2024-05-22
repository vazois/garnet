// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

#pragma warning disable IDE0005
using System;
using System.Collections.Generic;
#pragma warning restore IDE0005
using System.Runtime.CompilerServices;

namespace Garnet.cluster
{
    /// <summary>
    /// Cluster configuration
    /// </summary>
    internal sealed partial class ClusterConfig
    {
        /// <summary>
        /// Minimum hash slot value.
        /// </summary>
        public static readonly int MIN_HASH_SLOT_VALUE = 0;

        /// <summary>
        /// Maximum hash slot value.
        /// </summary>
        public static readonly int MAX_HASH_SLOT_VALUE = 16384;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="slot"></param>
        /// <returns></returns>
        public static bool OutOfRange(int slot) => slot >= MAX_HASH_SLOT_VALUE || slot < MIN_HASH_SLOT_VALUE;

        /// <summary>
        /// Num of workers assigned
        /// </summary>
        public int NumWorkers => workers.Length - 1;

        /// <summary>
        /// Get array of workers
        /// </summary>
        public Worker[] Workers => workers;

        readonly HashSlot[] slotMap;
        readonly Worker[] workers;

        /// <summary>
        /// Create default cluster config
        /// </summary>
        public ClusterConfig()
        {
            slotMap = new HashSlot[MAX_HASH_SLOT_VALUE];
            for (int i = 0; i < MAX_HASH_SLOT_VALUE; i++)
            {
                slotMap[i]._state = SlotState.OFFLINE;
                slotMap[i]._workerId = 0;
            }
            workers = new Worker[2];
            workers[0].Address = "unassigned";
            workers[0].Port = 0;
            workers[0].Nodeid = null;
            workers[0].ConfigEpoch = 0;
            workers[0].Role = NodeRole.UNASSIGNED;
            workers[0].ReplicaOfNodeId = null;
            workers[0].ReplicationOffset = 0;
            workers[0].Hostname = null;
        }

        /// <summary>
        /// Create cluster config
        /// </summary>
        /// <param name="slotMap"></param>
        /// <param name="workers"></param>
        public ClusterConfig(HashSlot[] slotMap, Worker[] workers)
        {
            this.slotMap = slotMap;
            this.workers = workers;
        }

        public ClusterConfig Copy()
        {
            var newSlotMap = new HashSlot[slotMap.Length];
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            Array.Copy(slotMap, newSlotMap, slotMap.Length);
            return new ClusterConfig(newSlotMap, newWorkers);
        }

        /// <summary>
        /// Check if workerId has assigned slots
        /// </summary>
        /// <param name="workerId">Offset in worker list.</param>
        /// <returns>True if worker has assigned slots, false otherwise.</returns>
        public bool HasAssignedSlots(ushort workerId)
        {
            for (ushort i = 0; i < 16384; i++)
            {
                if (slotMap[i].workerId == workerId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if the provided  slot is local from the perspective of the local config.
        /// 1. Local slots are assigned to workerId = 1
        /// 2. Local slots which are in migrating state are pointing to the target node thus workerdId != 1. 
        ///     However, we still need to redirect traffic as if the workerId == 1 until migration completes
        /// 3. Local slots for a replica are those slots served by its primary only for read operations
        /// </summary>
        /// <param name="slot">Slot to check</param>
        /// <param name="readCommand">If we are checking as a read command. Used to override check if READWRITE is specified</param>
        /// <returns>True if slot is owned by this node, false otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsLocal(ushort slot, bool readCommand = true)
            => slotMap[slot].workerId == 1 || IsLocalExpensive(slot, readCommand);

        private bool IsLocalExpensive(ushort slot, bool readCommand)
            => (readCommand && workers[1].Role == NodeRole.REPLICA && workers[slotMap[slot]._workerId].Nodeid.Equals(LocalNodePrimaryId, StringComparison.OrdinalIgnoreCase)) ||
            slotMap[slot]._state == SlotState.MIGRATING;

        /// <summary>
        /// Check if specified node-id belongs to a node in our local config.
        /// </summary>
        /// <param name="nodeid">Node id to search for.</param>
        /// <returns>True if node-id in worker list, false otherwise.</returns>
        public bool IsKnown(string nodeid)
        {
            for (int i = 1; i <= NumWorkers; i++)
                if (workers[i].Nodeid.Equals(nodeid, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Check if local node is a PRIMARY node
        /// </summary>
        public bool IsPrimary => LocalNodeRole == NodeRole.PRIMARY;

        /// <summary>
        /// Check if local node is a REPLICA node
        /// </summary>
        public bool IsReplica => LocalNodeRole == NodeRole.REPLICA;

        #region GetLocalNodeInfo
        /// <summary>
        /// Get local node ip
        /// </summary>
        /// <returns>IP of local worker</returns>
        public string LocalNodeIp => workers[1].Address;

        /// <summary>
        /// Get local node port
        /// </summary>
        /// <returns>Port of local worker</returns>
        public int LocalNodePort => workers[1].Port;

        /// <summary>
        /// Get local node cluster port
        /// </summary>
        /// <returns>Port of local worker</returns>
        public int LocalNodeClusterPort => workers[1].ClusterPort;

        /// <summary>
        /// Get local node ID
        /// </summary>
        /// <returns>Node-id of local worker.</returns>
        public string LocalNodeId => workers[1].Nodeid;

        /// <summary>
        /// Get local node role
        /// </summary>
        /// <returns>Role of local node.</returns>
        public NodeRole LocalNodeRole => workers[1].Role;

        /// <summary>
        /// Get nodeid of primary.
        /// </summary>
        /// <returns>Primary-id of the node this node is replicating.</returns>
        public string LocalNodePrimaryId => workers[1].ReplicaOfNodeId;

        /// <summary>
        /// Get config epoch for local worker.
        /// </summary>
        /// <returns>Config epoch of local node.</returns>
        public long LocalNodeConfigEpoch => workers[1].ConfigEpoch;

        /// <summary>
        /// Get local node replicas
        /// </summary>
        /// <returns>Returns a list of node-ids representing the replicas that replicate this node.</returns>
        public List<string> GetLocalNodeReplicaIds() => GetReplicaData(LocalNodeId);

        /// <summary>
        /// Get list of endpoints for all replicas of this node.
        /// </summary>
        /// <returns>List of (address,port) pairs.</returns>
        public List<(string, int)> GetLocalNodeReplicaEndpoints()
        {
            List<(string, int)> replicas = [];
            for (ushort i = 2; i < workers.Length; i++)
            {
                var replicaOf = workers[i].ReplicaOfNodeId;
                if (replicaOf != null && replicaOf.Equals(workers[1].Nodeid, StringComparison.OrdinalIgnoreCase))
                    replicas.Add((workers[i].Address, workers[i].ClusterPort));
            }
            return replicas;
        }

        /// <summary>
        /// Return all primary endpoints. Used from replica that is becoming a primary during a failover.
        /// </summary>
        /// <param name="includeMyPrimaryFirst"></param>
        /// <returns>List of pairs (address,port) representing known primary endpoints</returns>
        public List<(string, int)> GetLocalNodePrimaryEndpoints(bool includeMyPrimaryFirst = false)
        {
            var myPrimaryId = includeMyPrimaryFirst ? LocalNodePrimaryId : "";
            List<(string, int)> primaries = [];
            for (ushort i = 2; i < workers.Length; i++)
            {
                if (workers[i].Role == NodeRole.PRIMARY && !workers[i].Nodeid.Equals(myPrimaryId, StringComparison.OrdinalIgnoreCase))
                    primaries.Add((workers[i].Address, workers[i].ClusterPort));

                if (workers[i].Nodeid.Equals(myPrimaryId, StringComparison.OrdinalIgnoreCase))
                    primaries.Insert(0, (workers[i].Address, workers[i].ClusterPort));
            }
            return primaries;
        }

        /// <summary>
        /// Retrieve a list of slots served by this node's primary.
        /// </summary>
        /// <returns>List of slots.</returns>
        public List<int> GetLocalPrimarySlots()
        {
            var primaryId = LocalNodePrimaryId;
            List<int> result = [];
            for (var i = 0; i < MAX_HASH_SLOT_VALUE; i++)
            {
                if (workers[slotMap[i].workerId].Nodeid.Equals(primaryId, StringComparison.OrdinalIgnoreCase))
                    result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// Find maximum config epoch from local config
        /// </summary>
        /// <returns>Integer representing max config epoch value.</returns>
        public long GetMaxConfigEpoch()
        {
            long mx = 0;
            for (int i = 1; i <= NumWorkers; i++)
                mx = Math.Max(workers[i].ConfigEpoch, mx);
            return mx;
        }

        /// <summary>
        /// Retrieve list of all known node ids.
        /// </summary>
        /// <returns>List of strings representing known node ids.</returns>
        public List<string> GetRemoteNodeIds()
        {
            List<string> remoteNodeIds = new List<string>();
            for (int i = 2; i < workers.Length; i++)
                remoteNodeIds.Add(workers[i].Nodeid);
            return remoteNodeIds;
        }
        #endregion

        #region GetFromNodeId
        /// <summary>
        /// Get worker id from node id.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns>Integer representing offset of worker in worker list.</returns>
        public int GetWorkerIdFromNodeId(string nodeId)
        {
            for (ushort i = 1; i <= NumWorkers; i++)
            {
                if (workers[i].Nodeid.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        /// <summary>
        /// Get role from node-id.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns>Node role type</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NodeRole GetNodeRoleFromNodeId(string nodeId) => workers[GetWorkerIdFromNodeId(nodeId)].Role;

        /// <summary>
        /// Get worker from node-id.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns>Worker struct</returns>
        public Worker GetWorkerFromNodeId(string nodeId) => workers[GetWorkerIdFromNodeId(nodeId)];

        /// <summary>
        /// Get hostname from node-id.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns>String representing node's hostname.</returns>
        public string GetHostNameFromNodeId(string nodeId)
        {
            if (nodeId == null)
                return null;
            var workerId = GetWorkerIdFromNodeId(nodeId);
            return workerId == 0 ? null : workers[workerId].Hostname;
        }
        #endregion

        #region GetFromSlot
        /// <summary>
        /// Check if slot is set as IMPORTING
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <returns>True if slot is in IMPORTING state, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsImportingSlot(ushort slot) => slotMap[slot]._state == SlotState.IMPORTING;

        /// <summary>
        /// Check if slot is set as MIGRATING
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <returns>True if slot is in MIGRATING state, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMigratingSlot(ushort slot) => slotMap[slot]._state == SlotState.MIGRATING;

        /// <summary>
        /// Get slot state
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <returns>SlotState type</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SlotState GetState(ushort slot) => slotMap[slot]._state;

        /// <summary>
        /// Get worker offset in worker list from slot.
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <returns>Integer offset in worker list.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetWorkerIdFromSlot(ushort slot) => slotMap[slot].workerId;

        /// <summary>
        /// Get node-id of slot owner.
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <returns>String node-id</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetNodeIdFromSlot(ushort slot) => workers[slotMap[slot].workerId].Nodeid;

        /// <summary>
        /// Get config epoch from slot.
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <returns>Long value representing config epoch.</returns>
        public long GetConfigEpochFromSlot(int slot)
        {
            if (slotMap[slot].workerId < 0)
                return 0;
            return workers[slotMap[slot].workerId].ConfigEpoch;
        }
        #endregion

        /// <summary>
        /// Get formatted (using CLUSTER NODES format) cluster info.
        /// </summary>
        /// <returns>Formatted string.</returns>
        public string GetClusterInfo()
        {
            string nodes = "";
            for (ushort i = 1; i <= NumWorkers; i++)
                nodes += GetNodeInfo(i);
            return nodes;
        }

        private string GetFormattedShardInfo(uint primaryWorkerId, List<(ushort, ushort)> shardRanges, List<int> replicaWorkerIds)
        {
            var shardInfo = $"*4\r\n";

            shardInfo += $"$5\r\nslots\r\n";//1

            shardInfo += $"*{shardRanges.Count * 2}\r\n";//2
            for (var i = 0; i < shardRanges.Count; i++)
            {
                var range = shardRanges[i];
                shardInfo += $":{range.Item1}\r\n";
                shardInfo += $":{range.Item2}\r\n";
            }

            shardInfo += $"$5\r\nnodes\r\n";//3

            shardInfo += $"*{1 + replicaWorkerIds.Count}\r\n";//4
            shardInfo += GetFormattedNodeInfo(primaryWorkerId);
            foreach (var id in replicaWorkerIds)
                shardInfo += GetFormattedNodeInfo((uint)id);

            return shardInfo;
        }

        /// <summary>
        /// Get formatted (using CLUSTER SHARDS format) cluster config information.
        /// </summary>
        /// <returns>RESP formatted string</returns>
        public string GetShardsInfo()
        {
            var shardsInfo = "";
            var shardCount = 0;
            for (ushort i = 1; i <= NumWorkers; i++)
            {
                if (workers[i].Role == NodeRole.PRIMARY)
                {
                    var shardRanges = GetShardRanges(i);
                    var replicaWorkerIds = GetWorkerReplicas(i);
                    shardsInfo += GetFormattedShardInfo(i, shardRanges, replicaWorkerIds);
                    shardCount++;
                }
            }
            shardsInfo = $"*{shardCount}\r\n" + shardsInfo;
            return shardsInfo;
        }

        private string GetFormattedSlotInfo(int slotStart, int slotEnd, string address, int port, string nodeid, string hostname, List<string> replicaIds)
        {
            int countA = replicaIds.Count == 0 ? 3 : 3 + replicaIds.Count;
            var rangeInfo = $"*{countA}\r\n";

            rangeInfo += $":{slotStart}\r\n";
            rangeInfo += $":{slotEnd}\r\n";
            rangeInfo += $"*4\r\n${address.Length}\r\n{address}\r\n:{port}\r\n${nodeid.Length}\r\n{nodeid}\r\n";
            rangeInfo += $"*2\r\n$8\r\nhostname\r\n${hostname.Length}\r\n{hostname}\r\n";

            foreach (var replicaId in replicaIds)
            {
                var (replicaAddress, replicaPort) = GetWorkerDataEndpoint(replicaId);
                var replicaHostname = GetHostNameFromNodeId(replicaId);

                rangeInfo += $"*4\r\n${replicaAddress.Length}\r\n{replicaAddress}\r\n:{replicaPort}\r\n${replicaId.Length}\r\n{replicaId}\r\n";
                rangeInfo += $"*2\r\n$8\r\nhostname\r\n${replicaHostname.Length}\r\n{replicaHostname}\r\n";
            }
            return rangeInfo;
        }

        /// <summary>
        /// Get formatted (using CLUSTER SLOTS format) cluster config info.
        /// </summary>
        /// <returns>Formatted string.</returns>
        public string GetSlotsInfo()
        {
            string completeSlotInfo = "";
            int slotRanges = 0;
            int slotStart;
            int slotEnd;

            for (slotStart = 0; slotStart < slotMap.Length; slotStart++)
            {
                if (slotMap[slotStart]._state == SlotState.OFFLINE)
                    continue;

                for (slotEnd = slotStart; slotEnd < slotMap.Length; slotEnd++)
                {
                    if (slotMap[slotEnd]._state == SlotState.OFFLINE || slotMap[slotStart].workerId != slotMap[slotEnd].workerId)
                        break;
                }

                int currSlotWorkerId = slotMap[slotStart].workerId;
                var address = workers[currSlotWorkerId].Address;
                var port = workers[currSlotWorkerId].Port;
                var nodeid = workers[currSlotWorkerId].Nodeid;
                var hostname = workers[currSlotWorkerId].Hostname;
                var replicas = GetReplicaData(nodeid);
                slotEnd--;
                completeSlotInfo += GetFormattedSlotInfo(slotStart, slotEnd, address, port, nodeid, hostname, replicas);
                slotRanges++;
                slotStart = slotEnd;
            }
            completeSlotInfo = $"*{slotRanges}\r\n" + completeSlotInfo;
            //Console.WriteLine(completeSlotInfo);

            return completeSlotInfo;
        }

        /// <summary>
        /// Return a collection of worker node data for all nodes that are replicas of the node identified by the provided node id.
        /// </summary>
        /// <param name="nodeid">Node-id string.</param>
        /// <param name="formatted">Return a string collection using the CLUSTER NODES formatting otherwise return individual node ids.</param>
        /// <returns></returns>
        public List<string> GetReplicaData(string nodeid, bool formatted = false)
        {
            List<string> replicas = [];
            for (ushort i = 1; i < workers.Length; i++)
            {
                var replicaOf = workers[i].ReplicaOfNodeId;
                if (replicaOf != null && replicaOf.Equals(nodeid, StringComparison.OrdinalIgnoreCase))
                    replicas.Add(formatted ? GetNodeInfo(i) : workers[i].Nodeid);
            }
            return replicas;
        }

        /// <summary>
        /// Return count of slots in given state.
        /// </summary>
        /// <param name="slotState">SlotState type.</param>
        /// <returns>Integer representing count of slots in given state.</returns>
        public int GetSlotCountForState(SlotState slotState)
        {
            int count = 0;
            for (int i = 0; i < slotMap.Length; i++)
                count += slotMap[i]._state == slotState ? 1 : 0;
            return count;
        }

        /// <summary>
        /// Return number of primary nodes.
        /// </summary>
        /// <returns>Integer representing number of primary nodes.</returns>
        public int GetPrimaryCount()
        {
            var count = 0;
            for (ushort i = 1; i <= NumWorkers; i++)
            {
                var w = workers[i];
                count += w.Role == NodeRole.PRIMARY ? 1 : 0;
            }
            return count;
        }

        /// <summary>
        /// Get worker (IP address and port) for node-id.
        /// </summary>
        /// <param name="address">IP address string.</param>
        /// <param name="port">Port number.</param>
        /// <returns>String representing node-id matching endpoint.</returns>
        public string GetWorkerNodeIdFromAddress(string address, int port)
        {
            for (ushort i = 1; i <= NumWorkers; i++)
            {
                var w = workers[i];
                if (w.Address == address && w.Port == port)
                    return w.Nodeid;
            }
            return null;
        }
    }
}