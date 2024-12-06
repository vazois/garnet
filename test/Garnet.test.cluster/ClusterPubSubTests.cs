// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using StackExchange.Redis;

namespace Garnet.test.cluster
{
    [TestFixture(false), NonParallelizable]
    public unsafe class ClusterPubSubTests(bool UseTLS)
    {
        ClusterTestContext context;
        readonly string authPassword = null;
        readonly int defaultShards = 3;

        readonly Dictionary<string, LogLevel> monitorTests = [];

        [SetUp]
        public virtual void Setup()
        {
            context = new ClusterTestContext();
            context.Setup(monitorTests);
        }

        [TearDown]
        public virtual void TearDown()
        {
            context?.TearDown();
        }

        //[Test, Order(1)]
        //[Category("CLUSTER_PUBSUB")]
        public void ClusterPrimariesPubSub()
        {
            context.CreateInstances(defaultShards, useTLS: UseTLS, disablePubSub: false);
            context.CreateConnection(useTLS: UseTLS);
            _ = context.clusterTestUtils.SimpleSetupCluster(logger: context.logger);

            var multiplexer = context.clusterTestUtils.GetMultiplexer();
            var channelName = "wxz";
            var message = "Hello World!";
            var channel = RedisChannel.Literal("wxz");
            var sub = multiplexer.GetSubscriber();
            sub.Subscribe(channel, (receivedChannel, receivedMessage) =>
            {
                ClassicAssert.AreEqual(channelName, receivedChannel);
                ClassicAssert.AreEqual(message, receivedMessage);
            });

            context.clusterTestUtils.Publish(0, channel, message, logger: null);
        }
    }
}