// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Garnet.server;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            throw new Exception("Only single argument needed, path to rdb file!");
        }

        var rdbFilePath = args[0];
        var parser = new RDBParser();
        parser.Load(rdbFilePath);
    }
}