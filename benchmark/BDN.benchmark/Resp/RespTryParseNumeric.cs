// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Buffers.Text;
using System.Text;
using BenchmarkDotNet.Attributes;
using Garnet.common;

namespace BDN.benchmark.Resp
{
    public unsafe class RespTryParseNumeric
    {
        const int iterations = 1000;

        [Benchmark]
        [ArgumentsSource(nameof(LongAsciiValue))]
        public long LongTryParseCustom(byte[] input)
        {
            var result = 0L;
            for (var i = 0; i < iterations; i++)
            {
                fixed (byte* ptr = input)
                    _ = NumUtils.TryBytesToLong(input.Length, ptr, out result);
            }
            return result;
        }

        //#if NET8_0_OR_GREATER
        //        [Benchmark]
        //        [ArgumentsSource(nameof(LongAsciiValue))]
        //        public long LongTryParse(byte[] input)
        //        {
        //            var result = 0L;
        //            for (var i = 0; i < iterations; i++)
        //            {
        //                fixed (byte* ptr = input)
        //                    _ = long.TryParse(new Span<byte>(ptr, input.Length), out result);
        //            }

        //            return result;
        //        }
        //#endif

        [Benchmark]
        [ArgumentsSource(nameof(LongAsciiValue))]
        public long LongTryParseUtf8(byte[] input)
        {
            var result = 0L;
            for (var i = 0; i < iterations; i++)
            {
                fixed (byte* ptr = input)
                    _ = Utf8Parser.TryParse(new Span<byte>(ptr, input.Length), out result, out _);
            }

            return result;
        }

        public static IEnumerable<object> LongAsciiValue
            => ToAsciiNumber(SignedInt64Values);

        public static IEnumerable<byte[]> ToAsciiNumber(long[] integerValues)
            => integerValues.Select(value => Encoding.ASCII.GetBytes($"{value}"));

        public static long[] SignedInt64Values => [long.MinValue / 2, -1, 0, long.MaxValue / 2];
    }
}
