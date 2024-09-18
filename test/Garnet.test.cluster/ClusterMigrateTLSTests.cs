// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using NUnit.Framework;

namespace Garnet.test.cluster
{
    [TestFixture, NonParallelizable]
    public unsafe class ClusterTLSMT
    {
        ClusterMigrateTests tests;

        [SetUp]
        public void Setup()
        {
            tests = new ClusterMigrateTests(UseTLS: true);
            tests.Setup();
        }

        [TearDown]
        public void TearDown()
        {
            tests.TearDown();
            tests = null;
        }

        [Test, Order(1)]
        [Category("CLUSTER"), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        public void ClusterTLSInitialize()
            => tests.ClusterSimpleInitialize();

        [Test, Order(2)]
        [Category("CLUSTER"), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        public void ClusterTLSSlotInfo()
            => tests.ClusterSimpleSlotInfo();

        [Test, Order(3)]
        [Category("CLUSTER"), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        public void ClusterTLSAddDelSlots()
            => tests.ClusterAddDelSlots();

        [Test, Order(4)]
        [Category("CLUSTER"), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        public void ClusterTLSSlotChangeStatus()
            => tests.ClusterSlotChangeStatus();

        [Test, Order(5)]
        [Category("CLUSTER"), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        public void ClusterTLSRedirectMessage()
            => tests.ClusterRedirectMessage();

        [Test, Order(6)]
        [Category("CLUSTER"), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        public void ClusterTLSMigrateSlots()
            => tests.ClusterSimpleMigrateSlots();

        [Test, Order(7), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        [Category("CLUSTER")]
        public void ClusterTLSMigrateSlotsExpiry()
            => tests.ClusterSimpleMigrateSlotsExpiry();

        [Test, Order(8), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        [Category("CLUSTER")]
        public void ClusterTLSMigrateSlotsWithObjects()
            => tests.ClusterSimpleMigrateSlotsWithObjects();

        [Test, Order(9), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        [Category("CLUSTER")]
        public void ClusterTLSMigrateKeys()
            => tests.ClusterSimpleMigrateKeys();

        [Test, Order(10), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        [Category("CLUSTER")]
        public void ClusterTLSMigrateKeysWithObjects()
            => tests.ClusterSimpleMigrateKeysWithObjects();

        [Test, Order(11), CancelAfter(ClusterTestContext.clusterTestTimeout)]
        [Category("CLUSTER")]
        public void ClusterTLSMigratetWithReadWrite()
            => tests.ClusterSimpleMigrateWithReadWrite();
    }
}