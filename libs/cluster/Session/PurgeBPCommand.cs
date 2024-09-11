﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using Garnet.common;
using Garnet.server;
using Microsoft.Extensions.Logging;

namespace Garnet.cluster
{
    internal enum ManagerType : byte
    {
        MM, // MigrationManager
        RM, // ReplicationManager
    }

    /// <summary>
    /// Extension methods for <see cref="ManagerType"/>.
    /// </summary>
    internal static class ManagerTypeExtensions
    {
        public static string ToString(this ManagerType managerType)
        {
            return managerType switch
            {
                ManagerType.MM => "MigrationManager",
                ManagerType.RM => "ReplicationManager",
                _ => throw new GarnetException()
            };
        }
    }

    internal sealed unsafe partial class ClusterSession : IClusterSession
    {
        private bool TryPurgeBP(out bool invalidParameters)
        {
            invalidParameters = false;

            // Expecting exactly 1 argument
            if (parseState.Count != 1)
            {
                invalidParameters = true;
                return false;
            }

            if (!parseState.TryGetEnum<ManagerType>(0, ignoreCase: true, out var managerType) || !Enum.IsDefined(managerType))
            {
                while (!RespWriteUtils.WriteError(CmdStrings.RESP_SYNTAX_ERROR, ref dcurr, dend))
                    SendAndReset();
                return true;
            }

            try
            {
                var success = managerType switch
                {
                    ManagerType.MM => clusterProvider.migrationManager.Purge(),
                    ManagerType.RM => clusterProvider.replicationManager.Purge(),
                    _ => throw new GarnetException($"{managerType} not supported!")
                };

                if (success)
                {
                    while (!RespWriteUtils.WriteDirect(CmdStrings.RESP_OK, ref dcurr, dend))
                        SendAndReset();
                }
                else
                {
                    while (!RespWriteUtils.WriteError($"ERR Could not purge {managerType.ToString()}({managerType}) buffer pool because it is in use.", ref dcurr, dend))
                        SendAndReset();
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "PURGEBP {type}:{managerType}", managerType, managerType.ToString());
                while (!RespWriteUtils.WriteError($"ERR {ex.Message}", ref dcurr, dend))
                    SendAndReset();
                return true;
            }

            return true;
        }
    }
}