// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using CommandLine;

namespace AutoGenIntegration
{
    class Program
    {
        static async Task Chat(Options opts)
        {
            var llmConfig = LLMConfig.LoadFromJson(opts.ConfigPath);
            var agent = new Agent(llmConfig[0]);

            while (true)
            {
                Console.Write("[user]: ");
                var input = Console.ReadLine();

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                await agent.Ask(input);
            }
        }

        static void Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            if (result.Tag == ParserResultType.NotParsed) return;
            var opts = result.MapResult(o => o, xs => new Options());
            Task.Run(async () => await Chat(opts)).Wait();

        }
    }
}