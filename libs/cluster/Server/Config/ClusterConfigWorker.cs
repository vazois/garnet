// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Garnet.cluster
{
    internal sealed partial class ClusterConfig
    {
        /// <summary>
        /// Get address of worker at provided offset
        /// </summary>
        /// <param name="workerId">Workers array offset</param>
        /// <returns>Address of worker at offset</returns>
        public string GetWorkerAddress(uint workerId) => workers[workerId].Address;

        /// <summary>
        /// Get data port of worker at provided offset
        /// </summary>
        /// <param name="workerId">Workers array offset</param>
        /// <returns>Data port of worker at offset</returns>
        public int GetWorkerDataPort(uint workerId) => workers[workerId].Port;

        /// <summary>
        /// Get cluster port of worker at provided offset
        /// </summary>
        /// <param name="workerId">Workers array offset</param>
        /// <returns>Cluster port of worker at offset</returns>
        public int GetWorkerClusterPort(uint workerId) => workers[workerId].ClusterPort;

        /// <summary>
        /// Get node id of worker at provided offset
        /// </summary>
        /// <param name="workerId">Workers array offset</param>
        /// <returns>Node id of worker at offset</returns>
        public string GetWorkerNodeId(uint workerId) => workers[workerId].Nodeid;

        /// <summary>
        /// Get formatted (using CLUSTER NODES format) worker info.
        /// </summary>
        /// <param name="workerId">Offset of worker in the worker list.</param>
        /// <returns>Formatted string.</returns>
        public string GetNodeInfo(ushort workerId)
        {
            //<id>
            //<ip:port@cport[,hostname[,auxiliary_field=value]*]>
            //<flags>
            //<primary>
            //<ping-sent>
            //<pong-recv>
            //<config-epoch>
            //<link-state>
            //<slot> <slot> ... <slot>

            return $"{workers[workerId].Nodeid} " +
                $"{workers[workerId].Address}:{workers[workerId].Port}@{workers[workerId].ClusterPort},{workers[workerId].Hostname} " +
                $"{(workerId == 1 ? "myself," : "")}{(workers[workerId].Role == NodeRole.PRIMARY ? "master" : "slave")} " +
                $"{(workers[workerId].Role == NodeRole.REPLICA ? workers[workerId].ReplicaOfNodeId : "-")} " +
                $"0 " +
                $"0 " +
                $"{workers[workerId].ConfigEpoch} " +
                $"connected" +
                $"{GetSlotRange(workerId)}" +
                $"{GetSpecialStates(workerId)}\n";
        }

        private string GetSpecialStates(ushort workerId)
        {
            // Only print special states for local node
            if (workerId != 1) return "";
            string specialStates = "";
            for (int slot = 0; slot < slotMap.Length; slot++)
            {
                if (slotMap[slot]._state == SlotState.MIGRATING)
                {
                    // Get node-id of node that we are migrating to by using "transient" _workerId
                    specialStates += $" [{slot}->-{workers[slotMap[slot]._workerId].Nodeid}]";
                }
                else if (slotMap[slot]._state == SlotState.IMPORTING)
                {
                    specialStates += $" [{slot}-<-{GetNodeIdFromSlot((ushort)slot)}]";
                }
            }
            return specialStates;
        }

        /// <summary>
        /// Get worker offset in worker list for replicas of the given worker offset.
        /// </summary>
        /// <param name="workerId">Offset of worker in worker list.</param>
        /// <returns>List of worker offsets.</returns>
        private List<int> GetWorkerReplicas(uint workerId)
        {
            var primaryId = workers[workerId].Nodeid;
            List<int> replicaWorkerIds = new();
            for (ushort i = 1; i <= NumWorkers; i++)
            {
                string replicaOf = workers[i].ReplicaOfNodeId;
                if (replicaOf != null && replicaOf.Equals(primaryId, StringComparison.OrdinalIgnoreCase))
                    replicaWorkerIds.Add(i);
            }
            return replicaWorkerIds;
        }

        private string GetFormattedNodeInfo(uint workerId)
        {
            var nodeInfo = "*12\r\n";
            nodeInfo += "$2\r\nid\r\n";
            nodeInfo += $"$40\r\n{workers[workerId].Nodeid}\r\n";
            nodeInfo += "$4\r\nport\r\n";
            nodeInfo += $":{workers[workerId].Port}\r\n";
            nodeInfo += "$7\r\naddress\r\n";
            nodeInfo += $"${workers[workerId].Address.Length}\r\n{workers[workerId].Address}\r\n";
            nodeInfo += "$4\r\nrole\r\n";
            nodeInfo += $"${workers[workerId].Role.ToString().Length}\r\n{workers[workerId].Role}\r\n";
            nodeInfo += "$18\r\nreplication-offset\r\n";
            nodeInfo += $":{workers[workerId].ReplicationOffset}\r\n";
            nodeInfo += "$6\r\nhealth\r\n";
            nodeInfo += $"$6\r\nonline\r\n";
            return nodeInfo;
        }

        private string GetSlotRange(uint workerId)
        {
            string result = "";
            ushort start = ushort.MaxValue, end = 0;
            for (ushort i = 0; i < MAX_HASH_SLOT_VALUE; i++)
            {
                if (slotMap[i].workerId == workerId)
                {
                    if (i < start) start = i;
                    if (i > end) end = i;
                }
                else
                {
                    if (start != ushort.MaxValue)
                    {
                        if (end == start) result += $" {start}";
                        else result += $" {start}-{end}";
                        start = ushort.MaxValue;
                        end = 0;
                    }
                }
            }
            if (start != ushort.MaxValue)
            {
                if (end == start) result += $" {start}";
                else result += $" {start}-{end}";
            }
            return result;
        }

        private List<int> GetSlotList(uint workerId)
        {
            List<int> result = new();
            for (int i = 0; i < MAX_HASH_SLOT_VALUE; i++)
                if (slotMap[i].workerId == workerId) result.Add(i);
            return result;
        }

        /// <summary>
        /// Get shard slot ranges for worker.
        /// </summary>
        /// <param name="workerId">Offset of worker in worker list.</param>
        /// <returns>List of pairs representing slot ranges.</returns>
        private List<(ushort, ushort)> GetShardRanges(uint workerId)
        {
            List<(ushort, ushort)> ranges = new();
            ushort startRange = ushort.MaxValue;
            ushort endRange;
            for (ushort i = 0; i < MAX_HASH_SLOT_VALUE + 1; i++)
            {
                if (i < slotMap.Length && slotMap[i].workerId == workerId)
                    startRange = startRange == ushort.MaxValue ? i : startRange;
                else if (startRange != ushort.MaxValue)
                {
                    endRange = (ushort)(i - 1);
                    ranges.Add(new(startRange, endRange));
                    startRange = ushort.MaxValue;
                }
            }
            return ranges;
        }
    }
}
