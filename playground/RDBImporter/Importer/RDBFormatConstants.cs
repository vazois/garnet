// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Garnet.server
{
    internal static class RDBFormatConstants
    {
        /// <summary>
        /// RDB version that changes only when the current format is not backwards compatible
        /// </summary>
        public const int RDB_VERSION = 12;

        // Length encoding types
        public const byte RDB_6BITLEN = 0;
        public const byte RDB_14BITLEN = 1;
        public const byte RDB_32BITLEN = 0x80;
        public const byte RDB_64BITLEN = 0x81;
        public const byte RDB_ENCVAL = 3;
        public const ulong RDB_LENERR = ulong.MaxValue;

        // String object encodings
        public const byte RDB_ENC_INT8 = 0;
        public const byte RDB_ENC_INT16 = 1;
        public const byte RDB_ENC_INT32 = 2;
        public const byte RDB_ENC_LZF = 3;

        // RDB object types
        public const byte RDB_TYPE_STRING = 0;
        public const byte RDB_TYPE_LIST = 1;
        public const byte RDB_TYPE_SET = 2;
        public const byte RDB_TYPE_ZSET = 3;
        public const byte RDB_TYPE_HASH = 4;
        public const byte RDB_TYPE_ZSET_2 = 5;
        public const byte RDB_TYPE_MODULE_PRE_GA = 6;
        public const byte RDB_TYPE_MODULE_2 = 7;
        public const byte RDB_TYPE_HASH_ZIPMAP = 9;
        public const byte RDB_TYPE_LIST_ZIPLIST = 10;
        public const byte RDB_TYPE_SET_INTSET = 11;
        public const byte RDB_TYPE_ZSET_ZIPLIST = 12;
        public const byte RDB_TYPE_HASH_ZIPLIST = 13;
        public const byte RDB_TYPE_LIST_QUICKLIST = 14;
        public const byte RDB_TYPE_STREAM_LISTPACKS = 15;
        public const byte RDB_TYPE_HASH_LISTPACK = 16;
        public const byte RDB_TYPE_ZSET_LISTPACK = 17;
        public const byte RDB_TYPE_LIST_QUICKLIST_2 = 18;
        public const byte RDB_TYPE_STREAM_LISTPACKS_2 = 19;
        public const byte RDB_TYPE_SET_LISTPACK = 20;
        public const byte RDB_TYPE_STREAM_LISTPACKS_3 = 21;
        public const byte RDB_TYPE_HASH_METADATA = 22;
        public const byte RDB_TYPE_HASH_LISTPACK_EX = 23;

        public static bool rdbIsObjectTyperdbIsObjectType(byte t) => t is (>= 0 and <= 7) or (>= 9 and <= 23);

        // Special RDB opcodes
        public const byte RDB_OPCODE_SLOT_INFO = 244;
        public const byte RDB_OPCODE_FUNCTION2 = 245;
        public const byte RDB_OPCODE_FUNCTION_PRE_GA = 246;
        public const byte RDB_OPCODE_MODULE_AUX = 247;
        public const byte RDB_OPCODE_IDLE = 248;
        public const byte RDB_OPCODE_FREQ = 249;
        public const byte RDB_OPCODE_AUX = 250;
        public const byte RDB_OPCODE_RESIZEDB = 251;
        public const byte RDB_OPCODE_EXPIRETIME_MS = 252;
        public const byte RDB_OPCODE_EXPIRETIME = 253;
        public const byte RDB_OPCODE_SELECTDB = 254;
        public const byte RDB_OPCODE_EOF = 255;

        // Module serialized values sub opcodes
        public const byte RDB_MODULE_OPCODE_EOF = 0;
        public const byte RDB_MODULE_OPCODE_SINT = 1;
        public const byte RDB_MODULE_OPCODE_UINT = 2;
        public const byte RDB_MODULE_OPCODE_FLOAT = 3;
        public const byte RDB_MODULE_OPCODE_DOUBLE = 4;
        public const byte RDB_MODULE_OPCODE_STRING = 5;

        // Load function flags
        public const byte RDB_LOAD_NONE = 0;
        public const byte RDB_LOAD_ENC = 1 << 0;
        public const byte RDB_LOAD_PLAIN = 1 << 1;
        public const byte RDB_LOAD_SDS = 1 << 2;
        public const byte RDB_LOAD_HFLD = 1 << 3;
        public const byte RDB_LOAD_HFLD_TTL = 1 << 4;

        /// <summary>
        /// 
        /// </summary>
        public const byte RDBFLAGS_NONE = 0;
        public const byte RDBFLAGS_AOF_PREAMBLE = 1 << 0;
        public const byte RDBFLAGS_REPLICATION = 1 << 1;
        public const byte RDBFLAGS_ALLOW_DUP = 1 << 2;
        public const byte RDBFLAGS_FEED_REPL = 1 << 3;
        public const byte RDBFLAGS_KEEP_CACHE = 1 << 4;

        public const byte RDB_LOAD_ERR_EMPTY_KEY = 1;
        public const byte RDB_LOAD_ERR_EXPIRED_HASH = 2;
        public const byte RDB_LOAD_ERR_OTHER = 3;

    }
}
