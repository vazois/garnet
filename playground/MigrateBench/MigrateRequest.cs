// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics;
using Garnet.client;
using Microsoft.Extensions.Logging;

namespace MigrateBench
{
    public enum MigrateRequestType
    {
        KEYS,
        SLOTS,
        SLOTSRANGE
    }

    public class MigrateRequest
    {
        readonly Options opts;

        readonly string sourceAddress;
        readonly int sourcePort;
        readonly string targetAddress;
        readonly int targetPort;
        readonly int timeout = 100000;
        readonly ILogger logger;

        GarnetClientSession sourceNode;
        GarnetClientSession targetNode;

        public MigrateRequest(Options opts, ILogger logger = null)
        {
            this.opts = opts;
            var sourceEndpoint = opts.SourceEndpoint.Split(':');
            var targetEndpoint = opts.TargetEndpoint.Split(':');
            sourceAddress = sourceEndpoint[0];
            sourcePort = int.Parse(sourceEndpoint[1]);
            targetAddress = targetEndpoint[0];
            targetPort = int.Parse(targetEndpoint[1]);

            sourceNode = new(sourceAddress, sourcePort, bufferSize: 1 << 22);
            targetNode = new(targetAddress, targetPort, bufferSize: 1 << 22);
            this.logger = logger;
        }

        public void Run()
        {
            try
            {
                sourceNode.Connect();
                targetNode.Connect();
                var sourceNodeKeys = dbsize(ref sourceNode);
                var targetNodeKeys = dbsize(ref targetNode);
                logger?.LogInformation("SourceNode: {endpoint} KeyCount: {keys}", opts.SourceEndpoint, sourceNodeKeys);
                logger?.LogInformation("TargetNode: {endpoint} KeyCount: {keys}", opts.TargetEndpoint, targetNodeKeys);

                switch (opts.migrateRequestType)
                {
                    case MigrateRequestType.KEYS:
                        MigrateKeys();
                        break;
                    case MigrateRequestType.SLOTS:
                        break;
                    case MigrateRequestType.SLOTSRANGE:
                        MigrateSlotRanges();
                        break;
                    default:
                        throw new Exception("Error invalid migrate request type");
                }

                var sourceNodeKeys2 = dbsize(ref sourceNode);
                var targetNodeKeys2 = dbsize(ref targetNode);
                var keysTransferred = targetNodeKeys2 - targetNodeKeys;
                logger?.LogInformation("SourceNode: {endpoint} KeyCount after migration: {keys}", opts.SourceEndpoint, sourceNodeKeys2);
                logger?.LogInformation("TargetNode: {endpoint} KeyCount after migration: {keys}", opts.TargetEndpoint, targetNodeKeys2);
                logger?.LogInformation("KeysTransferred: {keys}", keysTransferred);

                int dbsize(ref GarnetClientSession c)
                {
                    var resp = c.ExecuteAsync("dbsize").GetAwaiter().GetResult();
                    return int.Parse(resp);
                }
            }
            finally
            {
                sourceNode.Dispose();
                targetNode.Dispose();
            }
        }

        private void MigrateSlotRanges()
        {
            try
            {
                var slots = opts.SlotRanges.ToArray();
                if ((slots.Length & 0x1) > 0)
                {
                    logger?.LogError("Malformed SLOTRANGES input; please provide pair of ranges");
                    return;
                }

                // migrate 192.168.1.20 7001 "" 0 5000 SLOTSRANGE 1000 7000            
                ICollection<string> request = ["MIGRATE", targetAddress, targetPort.ToString(), "", "0", timeout.ToString(), "SLOTSRANGE"];
                foreach (var slot in slots)
                    request.Add(slot.ToString());

                var startTimestamp = Stopwatch.GetTimestamp();
                logger?.LogInformation("Issuing MIGRATE SLOTSRANGE");
                var resp = sourceNode.ExecuteAsync([.. request]).GetAwaiter().GetResult();

                logger?.LogInformation("Waiting for Migration to Complete");
                resp = sourceNode.ExecuteAsync("CLUSTER", "MTASKS").GetAwaiter().GetResult();
                while (int.Parse(resp) > 0)
                {
                    Thread.Yield();
                    resp = sourceNode.ExecuteAsync("CLUSTER", "MTASKS").GetAwaiter().GetResult();
                }

                var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
                var t = TimeSpan.FromTicks(elapsed);
                logger?.LogInformation("SlotsRange Elapsed Time: {elapsed} seconds", t.TotalSeconds);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error at MigrateSlotsRange");
            }
        }

        private void MigrateKeys()
        {
            try
            {
                var slots = opts.SlotRanges.ToArray();
                if ((slots.Length & 0x1) > 0)
                {
                    logger?.LogError("Malformed SLOTRANGES input; please provide pair of ranges");
                    return;
                }

                List<int> ss = [];
                for (var i = 0; i < slots.Length; i += 2)
                    for (var j = slots[i]; j <= slots[i + 1]; j++)
                        ss.Add(j);
                slots = [.. ss];

                var sourceNodeId = sourceNode.ExecuteAsync("cluster", "myid").GetAwaiter().GetResult();
                var targetNodeId = targetNode.ExecuteAsync("cluster", "myid").GetAwaiter().GetResult();

                string[] migrating = ["cluster", "setslot", "<slot>", "migrating", targetNodeId];
                string[] importing = ["cluster", "setslot", "<slot>", "importing", sourceNodeId];
                string[] node = ["cluster", "setslot", "<slot>", "node", targetNodeId];
                string[] countkeysinslot = ["cluster", "countkeysinslot", "<slot>"];
                string[] getkeysinslot = ["cluster", "getkeysinslot", "<slot>", "<key-count>"];
                var resp = "";
                string[] respArray = null;

                var migratingTotalElapsed = 0.0;
                var importingTotalElapsed = 0.0;
                var countTotalElapsed = 0.0;
                var getTotalElapsed = 0.0;
                var migrateTotalElapsed = 0.0;
                var nodeTotalElapsed = 0.0;
                foreach (var slot in slots)
                {
                    var slotStr = slot.ToString();
                    migrating[2] = slotStr;
                    importing[2] = slotStr;
                    node[2] = slotStr;
                    countkeysinslot[2] = slotStr;
                    getkeysinslot[2] = slotStr;

                    importingTotalElapsed += Send(ref targetNode, ref importing, "importing", ref resp);
                    migratingTotalElapsed += Send(ref sourceNode, ref migrating, "migrating", ref resp);

                    countTotalElapsed += Send(ref sourceNode, ref countkeysinslot, "countkeysinslot", ref resp);
                    getkeysinslot[3] = resp;
                    getTotalElapsed += SendForArray(ref sourceNode, ref getkeysinslot, "getkeysinslot", ref respArray);
                    logger?.LogInformation("KeyCount to be transferred: {keys}", respArray.Length);

                    ICollection<string> migrate = ["MIGRATE", targetAddress, targetPort.ToString(), "", "0", timeout.ToString(), "KEYS"];
                    foreach (var key in respArray) migrate.Add(key);
                    var request = migrate.ToArray();
                    migrateTotalElapsed += Send(ref sourceNode, ref request, "migrate", ref resp);

                    nodeTotalElapsed += Send(ref targetNode, ref node, "node", ref resp);
                    nodeTotalElapsed += Send(ref sourceNode, ref node, "node", ref resp);
                }

                var totalElapsed = importingTotalElapsed + migratingTotalElapsed + countTotalElapsed + getTotalElapsed + migrateTotalElapsed + nodeTotalElapsed;
                logger?.LogInformation("KEYS importingTotalElapsed: {elapsed} seconds", importingTotalElapsed);
                logger?.LogInformation("KEYS migratingTotalElapsed: {elapsed} seconds", migratingTotalElapsed);
                logger?.LogInformation("KEYS countTotalElapsed: {elapsed} seconds", countTotalElapsed);
                logger?.LogInformation("KEYS getTotalElapsed: {elapsed} seconds", getTotalElapsed);
                logger?.LogInformation("KEYS migrateTotalElapsed: {elapsed} seconds", migrateTotalElapsed);
                logger?.LogInformation("KEYS nodeTotalElapsed: {elapsed} seconds", nodeTotalElapsed);
                logger?.LogInformation("KEYS Total Elapsed Time: {elapsed} seconds", totalElapsed);

                double Send(ref GarnetClientSession c, ref string[] request, string command, ref string resp)
                {
                    var startTimestamp = Stopwatch.GetTimestamp();
                    resp = c.ExecuteAsync(request).GetAwaiter().GetResult();
                    var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
                    var t = TimeSpan.FromTicks(elapsed);
                    logger?.LogInformation("{command} Elapsed Time: {elapsed} seconds", command, t.TotalSeconds);
                    return t.TotalSeconds;
                }

                double SendForArray(ref GarnetClientSession c, ref string[] request, string command, ref string[] resp)
                {
                    var startTimestamp = Stopwatch.GetTimestamp();
                    resp = c.ExecuteForArrayAsync(request).GetAwaiter().GetResult();
                    var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
                    var t = TimeSpan.FromTicks(elapsed);
                    logger?.LogInformation("{command} Elapsed Time: {elapsed} seconds", command, t.TotalSeconds);
                    return t.TotalSeconds;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error at MigrateKeys");
            }
        }
    }
}
