// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.ClientModel;
using System.Threading.Tasks;
using AutoGen;
using AutoGen.Core;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using OpenAI;

namespace AutoGenIntegration
{
    public class Agent
    {
        MiddlewareStreamingAgent agent;
        OpenAIClient client;
        MiddlewareAgent userProxyAgent;

        public Agent(LLMConfig config)
        {
            var keyCred = new ApiKeyCredential(config.ProviderKey);
            client = new OpenAIClient(keyCred);
            agent = new OpenAIChatAgent(
                name: "assistant",
                systemMessage: "You are an assistant that help user to do some tasks.",
                chatClient: client.GetChatClient(config.Model))
                .RegisterMessageConnector().RegisterPrintMessage();

            userProxyAgent = new UserProxyAgent(
                name: "user",
                humanInputMode: HumanInputMode.ALWAYS)
                .RegisterPrintMessage();
        }

        public async Task Ask(string question)
        {
            await userProxyAgent.InitiateChatAsync(receiver: agent, message: question, maxRound: 1);
        }
    }
}