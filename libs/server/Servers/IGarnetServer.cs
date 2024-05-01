// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using Garnet.networking;

namespace Garnet.server
{
    /// <summary>
    /// 
    /// </summary>
    public interface IGarnetServer : IDisposable
    {
        /// <summary>
        /// Register session provider for specified wire format with the server
        /// </summary>
        /// <param name="wireFormat"></param>
        /// <param name="backendProvider"></param>
        public void Register(WireFormat wireFormat, ISessionProvider backendProvider);

        /// <summary>
        /// Unregister provider associated with specified wire format
        /// </summary>
        /// <param name="wireFormat"></param>
        /// <param name="provider"></param>
        public void Unregister(WireFormat wireFormat, out ISessionProvider provider);

        /// <summary>
        /// Return a dictionary of WireFormat,ISession
        /// </summary>
        /// <returns></returns>
        public ConcurrentDictionary<WireFormat, ISessionProvider> GetSessionProviders();

        /// <summary>
        /// Add message consumer session
        /// </summary>
        /// <param name="protocol"></param>
        /// <param name="messageConsumerType"></param>
        /// <param name="provider"></param>
        /// <param name="networkSender"></param>
        /// <param name="session"></param>
        /// <returns></returns>
        public bool AddSession(WireFormat protocol, MessageConsumerType messageConsumerType, ref ISessionProvider provider, INetworkSender networkSender, out IMessageConsumer session);

        /// <summary>
        /// Start server
        /// </summary>
        public void Start();
    }
}