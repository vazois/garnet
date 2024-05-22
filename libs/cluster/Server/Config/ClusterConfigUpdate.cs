// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Garnet.cluster
{
    internal sealed partial class ClusterConfig
    {
        /// <summary>
        /// Initialize local worker with provided information
        /// </summary>
        /// <param name="nodeId">Local worker node-id.</param>
        /// <param name="address">Local worker IP address.</param>
        /// <param name="port">Local worker port.</param>
        /// <param name="configEpoch">Local worker config epoch.</param>
        /// <param name="currentConfigEpoch">Local worker current config epoch.</param>
        /// <param name="lastVotedConfigEpoch">Local worker last voted epoch.</param>
        /// <param name="role">Local worker role.</param>
        /// <param name="replicaOfNodeId">Local worker primary id.</param>
        /// <param name="hostname">Local worker hostname.</param>
        /// <param name="clusterPort">Local worker hostname.</param>
        /// <returns>Instance of local config with update local worker info.</returns>
        public ClusterConfig InitializeLocalWorker(
            string nodeId,
            string address,
            int port,
            long configEpoch,
            long currentConfigEpoch,
            long lastVotedConfigEpoch,
            NodeRole role,
            string replicaOfNodeId,
            string hostname,
            int clusterPort)
        {
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            newWorkers[1].Address = address;
            newWorkers[1].Port = port;
            newWorkers[1].Nodeid = nodeId;
            newWorkers[1].ConfigEpoch = configEpoch;
            newWorkers[1].CurrentConfigEpoch = currentConfigEpoch;
            newWorkers[1].LastVotedConfigEpoch = lastVotedConfigEpoch;
            newWorkers[1].Role = role;
            newWorkers[1].ReplicaOfNodeId = replicaOfNodeId;
            newWorkers[1].ReplicationOffset = 0;
            newWorkers[1].Hostname = hostname;
            newWorkers[1].ClusterPort = clusterPort;
            return new ClusterConfig(slotMap, newWorkers);
        }

        private ClusterConfig InPlaceUpdateWorker(
            string nodeid,
            string address,
            int port,
            long configEpoch,
            long currentConfigEpoch,
            long lastVotedConfigEpoch,
            NodeRole role,
            string replicaOfNodeId,
            string hostname,
            int clusterPort,
            List<int> slots)
        {
            ushort workerId = 0;
            for (int i = 1; i < workers.Length; i++)
            {
                if (workers[i].Nodeid.Equals(nodeid, StringComparison.OrdinalIgnoreCase))
                {
                    //Skip update if received config is smaller or equal than local worker epoch
                    //Update only if received config epoch is strictly greater
                    if (configEpoch <= workers[i].ConfigEpoch) return this;
                    workerId = (ushort)i;
                    break;
                }
            }

            Worker[] newWorkers = this.workers;
            if (workerId == 0)
            {
                newWorkers = new Worker[workers.Length + 1];
                workerId = (ushort)workers.Length;
                Array.Copy(workers, newWorkers, workers.Length);
            }

            newWorkers[workerId].Address = address;
            newWorkers[workerId].Port = port;
            newWorkers[workerId].Nodeid = nodeid;
            newWorkers[workerId].ConfigEpoch = configEpoch;
            newWorkers[workerId].CurrentConfigEpoch = currentConfigEpoch;
            newWorkers[workerId].LastVotedConfigEpoch = lastVotedConfigEpoch;
            newWorkers[workerId].Role = role;
            newWorkers[workerId].ReplicaOfNodeId = replicaOfNodeId;
            newWorkers[workerId].Hostname = hostname;
            newWorkers[workerId].ClusterPort = clusterPort;

            var newSlotMap = this.slotMap;
            if (slots != null)
            {
                foreach (int slot in slots)
                {
                    newSlotMap[slot]._workerId = workerId;
                    newSlotMap[slot]._state = SlotState.STABLE;
                }
            }

            return new ClusterConfig(newSlotMap, newWorkers);
        }

        /// <summary>
        /// Update replication offset lazily.
        /// </summary>
        /// <param name="newReplicationOffset">Long of new replication offset.</param>
        public void LazyUpdateLocalReplicationOffset(long newReplicationOffset)
            => workers[1].ReplicationOffset = newReplicationOffset;

        /// <summary>
        /// Merging incoming configuration from gossip with local configuration copy.
        /// </summary>
        /// <param name="other">Incoming config object.</param>
        /// <param name="workerBanList">Worker ban list used to prevent merging.</param>
        /// <returns>Cluster config object.</returns>
        public ClusterConfig Merge(ClusterConfig other, ConcurrentDictionary<string, long> workerBanList)
        {
            var localId = LocalNodeId;
            var newConfig = this;
            for (ushort i = 1; i < other.NumWorkers + 1; i++)
            {
                //Do not update local node config
                if (localId.Equals(other.workers[i].Nodeid, StringComparison.OrdinalIgnoreCase))
                    continue;
                //Skip any nodes scheduled for deletion
                if (workerBanList.ContainsKey(other.workers[i].Nodeid))
                    continue;

                newConfig = newConfig.InPlaceUpdateWorker(
                    other.workers[i].Nodeid,
                    other.workers[i].Address,
                    other.workers[i].Port,
                    other.workers[i].ConfigEpoch,
                    other.workers[i].CurrentConfigEpoch,
                    other.workers[i].LastVotedConfigEpoch,
                    other.workers[i].Role,
                    other.workers[i].ReplicaOfNodeId,
                    other.workers[i].Hostname,
                    other.workers[i].ClusterPort,
                    other.GetSlotList(i));
            }
            return newConfig;
        }

        /// <summary>
        /// Remove worker
        /// </summary>
        /// <param name="nodeid">Node-id string.</param>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig RemoveWorker(string nodeid)
        {
            ushort workerId = 0;
            for (int i = 1; i < workers.Length; i++)
            {
                if (workers[i].Nodeid.Equals(nodeid, StringComparison.OrdinalIgnoreCase))
                {
                    workerId = (ushort)i;
                    break;
                }
            }

            var newSlotMap = new HashSlot[MAX_HASH_SLOT_VALUE];
            Array.Copy(slotMap, newSlotMap, slotMap.Length);
            for (int i = 0; i < newSlotMap.Length; i++)
            {
                if (newSlotMap[i].workerId == workerId)
                {
                    newSlotMap[i]._workerId = 0;
                    newSlotMap[i]._state = SlotState.OFFLINE;
                }
                else if (newSlotMap[i].workerId > workerId)
                {
                    newSlotMap[i]._workerId--;
                }
            }

            Worker[] newWorkers = new Worker[workers.Length - 1];
            Array.Copy(workers, 0, newWorkers, 0, workerId);
            if (workers.Length - 1 != workerId)
                Array.Copy(workers, workerId + 1, newWorkers, workerId, workers.Length - workerId - 1);

            return new ClusterConfig(newSlotMap, newWorkers);
        }

        /// <summary>
        /// Make this worker replica of a node with specified node Id.
        /// </summary>
        /// <param name="nodeid">String node-id of primary.</param>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig MakeReplicaOf(string nodeid)
        {
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);

            newWorkers[1].ReplicaOfNodeId = nodeid;
            newWorkers[1].Role = NodeRole.REPLICA;
            return new ClusterConfig(slotMap, newWorkers);
        }

        /// <summary>
        /// Set role of local worker.
        /// </summary>
        /// <param name="role"></param>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig SetLocalWorkerRole(NodeRole role)
        {
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);

            newWorkers[1].Role = role;
            return new ClusterConfig(slotMap, newWorkers);
        }

        /// <summary>
        /// Take over for primary.
        /// </summary>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig TakeOverFromPrimary()
        {
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            newWorkers[1].Role = NodeRole.PRIMARY;
            newWorkers[1].ReplicaOfNodeId = null;

            var slots = GetLocalPrimarySlots();
            var newSlotMap = new HashSlot[MAX_HASH_SLOT_VALUE];
            Array.Copy(slotMap, newSlotMap, slotMap.Length);
            foreach (int slot in slots)
            {
                newSlotMap[slot]._workerId = 1;
                newSlotMap[slot]._state = SlotState.STABLE;
            }

            return new ClusterConfig(newSlotMap, newWorkers);
        }

        /// <summary>
        /// Try to make local node owner of list of slots given.
        /// </summary>
        /// <param name="slots">Slots to assign.</param>
        /// <param name="slotAssigned">Slot already assigned if any during this bulk op.</param>
        /// <param name="config">ClusterConfig object with updates</param>
        /// <param name="state">SlotState type to be set.</param>
        /// <returns><see langword="false"/> if slot already owned by someone else according to a message received from the gossip protocol; otherwise <see langword="true"/>.</returns>
        public bool TryAddSlots(HashSet<int> slots, out int slotAssigned, out ClusterConfig config, SlotState state = SlotState.STABLE)
        {
            slotAssigned = -1;
            config = null;

            var newSlotMap = new HashSlot[16384];
            Array.Copy(slotMap, newSlotMap, slotMap.Length);
            if (slots != null)
            {
                foreach (int slot in slots)
                {
                    if (newSlotMap[slot].workerId != 0)
                    {
                        slotAssigned = slot;
                        return false;
                    }
                    newSlotMap[slot]._workerId = 1;
                    newSlotMap[slot]._state = state;
                }
            }

            config = new ClusterConfig(newSlotMap, workers);
            return true;
        }

        /// <summary>
        /// Try to remove slots from this local node.
        /// </summary>
        /// <param name="slots">Slots to be removed.</param>
        /// <param name="notLocalSlot">The slot number that is not local.</param>
        /// <param name="config">ClusterConfig object with updates</param>
        /// <returns><see langword="false"/> if a slot provided is not local; otherwise <see langword="true"/>.</returns>
        public bool TryRemoveSlots(HashSet<int> slots, out int notLocalSlot, out ClusterConfig config)
        {
            notLocalSlot = -1;
            config = null;

            var newSlotMap = new HashSlot[16384];
            Array.Copy(slotMap, newSlotMap, slotMap.Length);
            if (slots != null)
            {
                foreach (int slot in slots)
                {
                    if (newSlotMap[slot].workerId == 0)
                    {
                        notLocalSlot = slot;
                        return false;
                    }
                    newSlotMap[slot]._workerId = 0;
                    newSlotMap[slot]._state = SlotState.OFFLINE;
                }
            }

            config = new ClusterConfig(newSlotMap, workers);
            return true;
        }

        /// <summary>
        /// Update local slot state.
        /// </summary>
        /// <param name="slot">Slot number to update state</param>
        /// <param name="workerId">Worker offset information associated with slot.</param>
        /// <param name="state">SlotState type</param>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig UpdateSlotState(int slot, int workerId, SlotState state)
        {
            var newSlotMap = new HashSlot[16384];
            Array.Copy(slotMap, newSlotMap, slotMap.Length);

            newSlotMap[slot]._workerId = (ushort)workerId;
            newSlotMap[slot]._state = state;
            return new ClusterConfig(newSlotMap, workers);
        }

        /// <summary>
        /// Update slot states in bulk.
        /// </summary>
        /// <param name="slots">Slot numbers to update state.</param>
        /// <param name="workerId">Worker offset information associated with slot.</param>
        /// <param name="state">SlotState type</param>
        /// <returns>ClusterConfig object with updates.</returns>        
        public ClusterConfig UpdateMultiSlotState(HashSet<int> slots, int workerId, SlotState state)
        {
            var newSlotMap = new HashSlot[MAX_HASH_SLOT_VALUE];
            Array.Copy(slotMap, newSlotMap, slotMap.Length);

            foreach (var slot in slots)
            {
                newSlotMap[slot]._workerId = (ushort)workerId;
                newSlotMap[slot]._state = state;
            }
            return new ClusterConfig(newSlotMap, workers);
        }

        /// <summary>
        /// Update config epoch for worker in new version of config.
        /// </summary>
        /// <param name="configEpoch">Config epoch value to set.</param>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig SetLocalWorkerConfigEpoch(long configEpoch)
        {
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);

            // Ensure epoch is zero and monotonicity
            if (workers[1].ConfigEpoch == 0 && workers[1].ConfigEpoch < configEpoch)
            {
                newWorkers[1].ConfigEpoch = configEpoch;
            }
            else
                return null;

            return new ClusterConfig(slotMap, newWorkers);
        }

        /// <summary>
        /// Increment local config epoch without consensus
        /// </summary>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig BumpLocalNodeConfigEpoch()
        {
            long maxConfigEpoch = GetMaxConfigEpoch();
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            newWorkers[1].ConfigEpoch = maxConfigEpoch + 1;
            return new ClusterConfig(slotMap, newWorkers);
        }

        /// <summary>
        /// Bump current config epoch for voting
        /// </summary>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig BumpLocalNodeCurrentConfigEpoch()
        {
            long nextValidConfigEpoch = LocalNodeCurrentConfigEpoch;
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            newWorkers[1].CurrentConfigEpoch = nextValidConfigEpoch == 0 ? GetMaxConfigEpoch() : nextValidConfigEpoch + 1;
            return new ClusterConfig(slotMap, newWorkers);
        }

        /// <summary>
        /// Check if sender has same local worker epoch as the receiver node and resolve collision.
        /// </summary>
        /// <param name="other">Incoming configuration object.</param>        
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig HandleConfigEpochCollision(ClusterConfig other)
        {
            //if incoming config epoch different than local don't need to do anything
            if (LocalNodeConfigEpoch != other.LocalNodeConfigEpoch)
                return this;

            var remoteNodeId = other.LocalNodeId;
            var localNodeId = LocalNodeId;

            //if localNodeId is smaller then do nothing
            if (localNodeId.CompareTo(remoteNodeId) <= 0) return this;

            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            newWorkers[1].ConfigEpoch++;
            return new ClusterConfig(slotMap, newWorkers);
        }

        /// <summary>
        /// Updated last voted epoch to requested epoch.
        /// </summary>
        /// <param name="requestedEpoch">Requested epoch value.</param>
        /// <returns>ClusterConfig object with updates.</returns>
        public ClusterConfig SetLocalNodeLastVotedConfigEpoch(long requestedEpoch)
        {
            var newWorkers = new Worker[workers.Length];
            Array.Copy(workers, newWorkers, workers.Length);
            newWorkers[1].LastVotedConfigEpoch = requestedEpoch;
            return new ClusterConfig(slotMap, newWorkers);
        }
    }
}
