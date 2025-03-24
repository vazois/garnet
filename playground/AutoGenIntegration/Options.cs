// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using CommandLine;

namespace AutoGenIntegration
{
    public class Options
    {
        [Option("config", Required = true, HelpText = "LLM config")]
        public string ConfigPath { get; set; }
    }
}
