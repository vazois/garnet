// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Garnet.server
{
    public readonly unsafe struct ByteArraySpan : IEquatable<ByteArraySpan>
    {
        public readonly byte[] array;
        public readonly unsafe byte* ptr;
        public readonly int length;

        public ByteArraySpan(ReadOnlySpan<byte> span)
        {
            this.ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));
            this.length = span.Length;
        }

        public unsafe ByteArraySpan(byte[] array)
            : this(array.AsSpan())
        {
            this.array = array;
        }

        public bool Equals(ByteArraySpan other)
        {
            if (other.length != this.length)
                return false;

            var a = ptr;
            var b = other.ptr;
            var end = a + length;

            while (a < end)
                if (*a++ != *b++)
                    return false;

            return true;
        }

        public override int GetHashCode()
        {
            return (int)HashBytes(ptr, length);

            long HashBytes(byte* pbString, int len)
            {
                const long magicno = 40343;
                char* pwString = (char*)pbString;
                int cbBuf = len / 2;
                ulong hashState = (ulong)len;

                for (int i = 0; i < cbBuf; i++, pwString++)
                    hashState = magicno * hashState + *pwString;

                if ((len & 1) > 0)
                {
                    byte* pC = (byte*)pwString;
                    hashState = magicno * hashState + *pC;
                }

                return (long)BitOperations.RotateRight(magicno * hashState, 4);
            }
        }
    }
}
