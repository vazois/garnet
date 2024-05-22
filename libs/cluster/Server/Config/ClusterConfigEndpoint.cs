// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Garnet.cluster
{
    internal sealed partial class ClusterConfig
    {
        /// <summary>
        /// Return data endpoint of primary if this node is a replica.
        /// </summary>
        /// <returns>Returns primary endpoints if this node is a replica, otherwise (null,-1)</returns>
        public (string address, int port) GetLocalNodePrimaryDataEndpoint() => GetWorkerDataEndpoint(workers[1].ReplicaOfNodeId);

        /// <summary>
        /// Get worker endpoint (IP address and port) from node-id for data operations.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns>Pair of (string,int) representing worker endpoint.</returns>
        public (string address, int port) GetWorkerDataEndpoint(string nodeId)
        {
            if (nodeId == null)
                return (null, -1);
            var workerId = GetWorkerIdFromNodeId(nodeId);
            return workerId == 0 ? (null, -1) : (workers[workerId].Address, workers[workerId].Port);
        }

        /// <summary>
        /// Get data endpoint of slot owner.
        /// </summary>
        /// <param name="slot">Slot number.</param>
        /// <param name="ask">Indicates whether the endpoint returned will be used for ASK redirect or otherwise</param>
        /// <returns>Pair of (string,integer) representing endpoint.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (string address, int port) GetWorkerDataEndpoint(ushort slot, bool ask = false)
        {
            // If used with ask enabled get target node endpoint
            var workerId = ask ? slotMap[slot]._workerId : slotMap[slot].workerId;
            return (workers[workerId].Address, workers[workerId].Port);
        }

        /// <summary>
        /// Get endpoint (IP address and port) from node-id for cluster operations.
        /// </summary>
        /// <param name="nodeId">Node-id.</param>
        /// <returns>Pair of (string,integer) representing endpoint.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (string address, int port) GetWorkerClusterEndpoint(string nodeId)
        {
            if (nodeId == null) return (null, -1);
            var workerId = GetWorkerIdFromNodeId(nodeId);
            return workerId == 0 ? (null, -1) : (workers[workerId].Address, workers[workerId].ClusterPort);
        }
    }
}
