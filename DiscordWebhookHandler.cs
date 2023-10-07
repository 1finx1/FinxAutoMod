using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using Steamworks;

namespace FinxSpamChatFilter
{
    public class FinxDiscordWebhookHandler : IDisposable
    {
        private readonly FinxSpamChatFilterConfig pluginConfig;
        private readonly ConcurrentQueue<List<string>> messageQueue;
        private readonly Timer timer;
        
        public FinxDiscordWebhookHandler(List<FinxDiscordWebhookConfig> webhookConfigs)
        {
            pluginConfig = new FinxSpamChatFilterConfig { DiscordWebhooks = webhookConfigs };
            messageQueue = new ConcurrentQueue<List<string>>();
            timer = new Timer(SendMessages, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        public void EnqueueMessages(List<string> messages)
        {
            messageQueue.Enqueue(new List<string>(messages)); // Create a deep copy and enqueue it
        }

        private void SendMessages(object state)
        {
            while (messageQueue.TryDequeue(out List<string> messages))
            {
                if (messages.Count == 0)
                {
                    continue;
                }

                var embeds = new List<dynamic>();

                // Create the embed for blacklisted messages
                var webhookMessage = new
                {
                    title = "Blacklisted/Spammed Messages", // Change the title here
                    color = 2269951,
                    description = string.Join("\n", messages),
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH\\:mm\\:ss.fffffffzzz"),
                    author = new
                    {
                        // Any author information you want to include
                    }
                };

                embeds.Add(webhookMessage);

                var allWebhookMessages = new
                {
                    embeds = embeds.ToArray()
                };

                foreach (var hook in pluginConfig.DiscordWebhooks)
                {
                    if (hook.Enabled)
                    {
                        SendToDiscord(hook.Url, allWebhookMessages);
                    }
                }
            }
        }


        private void SendToDiscord(string webhookUrl, object message)
        {
            var request = WebRequest.Create(webhookUrl);
            request.Method = "POST";
            request.ContentType = "application/json";

            using (var writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(JsonConvert.SerializeObject(message));
                writer.Flush();
            }

            using (var response = request.GetResponse())
            {
                // Optionally handle the response if needed.
            }
        }



        public void Dispose()
        {
            timer.Dispose();
        }
    }
}
