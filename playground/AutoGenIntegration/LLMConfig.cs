// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Garnet.common;

namespace AutoGenIntegration
{
    public class LLMConfig
    {
        [JsonPropertyName("ProviderKey")]
        public string ProviderKey { get; private set; }

        [JsonPropertyName("Model")]
        public string Model { get; private set; }

        [JsonPropertyName("SystemMessage")]
        public string SystemMessage { get; private set; }

        public static LLMConfig[] LoadFromJson(string configPath)
        {
            var streamProvider = StreamProviderFactory.GetStreamProvider(FileLocationType.Local);
            using var stream = streamProvider.Read(configPath);
            using var streamReader = new StreamReader(stream);

            var respJson = streamReader.ReadToEnd();
            var llmConfig = JsonSerializer.Deserialize<LLMConfig[]>(respJson);
            return llmConfig;
        }

        public LLMConfig(string providerKey, string model, string systemMessage)
        {
            ProviderKey = providerKey;
            Model = model;
            SystemMessage = systemMessage;
        }
    }
}