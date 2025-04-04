---
id: compatibility
sidebar_label: Compatibility
title: API Compatibility
---

Garnet is a cache-store with a new thread-scalable system architecture. The network, processing, and storage (memory
and disk/cloud) layers of Garnet are all designed from the ground up. We chose the RESP API as a matter of
convenience given its broad adoption in the community. Garnet is not intended to be a 100% perfect drop-in 
replacement for Redis, rather it should be regarded as a close-enough starting point for you to ensure compatibility
for features that matter to you. Garnet does work unmodified with many Redis clients (we have in particular tested
Garnet with `StackExchange.Redis` very well), so getting started is very easy.

A list of API calls supported today by Garnet is maintained [here](../commands/api-compatibility.md). Below we highlight 
specific non-API-related choices that may not be compatible. This list is not exhaustive, rather it is meant as a broad
guideline on what differences you can expect when using Garnet.

1. Garnet being multi-threaded, `MSET` is not atomic. For an atomic version of `MSET`, you would need to express
it as a transaction (stored procedure). This is easily fixable, similar to `MSETNX` [here](https://github.com/microsoft/garnet/pull/996), let us know in issues if you
prefer the atomic semantics of `MSET` by default.
3. Garnet does not support Redis modules. Instead, it has its own C# based [extensibility](../extensions/overview.md) mechanisms
that are optimized for very high performance, thread scalability, safety, and ease of use.
4. Garnet now has full-fledged and efficient support for Lua scripting. You can enable Lua with the `--lua` switch.
5. Garnet respects the FIFO ordering of request-responses. However, when used with larger-than-memory data, and if you
_opt in_ to using the scatter-gather version of IO (using the `EnableScatterGatherGet [--sg-get]` option) for increased disk performance, then
even though results are still returned in FIFO order, the read operations may be executed out-of-order to earlier
write operations in the same input operation sequence.
6. When Garnet is used with the append-only-file (AOF) turned on, by default the server does not wait for commit before
   returning success to the user. This can be adjusted using the `WaitForCommit [--aof-commit-wait]` option, while the frequency of
   commit can be tuned using the `CommitFrequencyMs [--aof-commit-freq]` option.
7. You can disable the object store if your workload only consists of raw string operations, using the option `DisableObjects [--no-obj]`. The
   storage tier is disabled by default, and you can enable it using `EnableStorageTier [--storage-tier]`. You can disable the pub-sub feature
   using the option `DisablePubSub [--no-pubsub]`.
