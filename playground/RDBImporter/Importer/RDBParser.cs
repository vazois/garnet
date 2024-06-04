// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text;

namespace Garnet.server
{
    public class RDBParser
    {
        private readonly int bufferSize;

        public RDBParser(int bufferSize = 1 << 15)
        {
            this.bufferSize = bufferSize;
        }

        /// <summary>
        /// Load RDB file data
        /// </summary>
        /// <param name="rdbFilePath"></param>
        /// <exception cref="Exception"></exception>
        public void Load(string rdbFilePath)
        {
            // Attempt to load ACL configuration file
            if (!File.Exists(rdbFilePath))
            {
                throw new Exception($"Cannot find RDB file '{rdbFilePath}'");
            }

            using (Stream source = File.OpenRead(rdbFilePath))
            {
                var iob = new byte[bufferSize];
                var readOffset = 0;
                int bytesRead;

                if (!((bytesRead = source.Read(iob, readOffset, 9)) > 0))
                {
                    throw new Exception("Error reading RDB header!");
                }

                Span<byte> header = iob;
                // Sanity check header
                if (!header.Slice(0, 5).SequenceEqual("REDIS"u8))
                    throw new Exception("Error missing REDIS header!");

                // Parse RDB version
                if (!int.TryParse(Encoding.ASCII.GetString(header.Slice(5, 4)), out var version))
                    throw new Exception("Error reading version of RDB file!");

                readOffset += bytesRead;
            }
        }

        private void ParseInternal(byte[] ioBuffer, int readHead, int byteRead)
        {

        }
    }
}
