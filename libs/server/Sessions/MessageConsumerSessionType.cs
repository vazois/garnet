// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Garnet.server
{
    /// <summary>
    /// Message consumer session type
    /// </summary>
    public enum MessageConsumerType : byte
    {
        /// <summary>
        /// Session of type RespServerSession
        /// </summary>
        RespSessionConsumer,
        /// <summary>
        /// Session of type ClusterSession
        /// </summary>
        ClusterSessionConsumer,
    }
}