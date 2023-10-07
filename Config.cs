using Rocket.API;
using System.Collections.Generic;

namespace FinxSpamChatFilter
{
    public class FinxSpamChatFilterConfig : IRocketPluginConfiguration
    {
        public int MaxWarnings { get; set; } // maximum number of warnings you can have before AutoMuteDuration kicks in
        public int AutoMuteDuration { get; set; } // time to be muted for saying bad words
        public int SpamMuteDuration { get; set; } // time to be muted for spamming
        public int MaxMessagesPerDuration { get; set; } // max messages that can be sent per duration 
        public int MaxMessagesDuration { get; set; } // set the duration 

        public bool EnableMuteWorldBroadcasts { get; set; } = true;

        public bool PunishmentsEnabled { get; set; }

        public List<string> WhitelistedWords { get; set; }

        public List<string> WhitelistedCommands { get; set; }

        public List<string> BadWords { get; set; }
        public List<FinxDiscordWebhookConfig> DiscordWebhooks { get; set; }

        public List<string> BlacklistedCommands { get; set; }

        public void LoadDefaults()
        {
            EnableMuteWorldBroadcasts = true;
            MaxWarnings = 4;
            AutoMuteDuration = 15;
            SpamMuteDuration = 15;
            PunishmentsEnabled = true;
            MaxMessagesPerDuration = 10;
            MaxMessagesDuration = 10;
            BadWords = new List<string> { "nig" };
            BlacklistedCommands = new List<string>
            {
                "heal",
                "compass",
                "rocket",
                "give"

            };
            DiscordWebhooks = new List<FinxDiscordWebhookConfig>
            {
                new FinxDiscordWebhookConfig { Url = "https://discord.com/api/webhooks/1142874161841188954/ozFEEQyV8sPxyRnhlApHDHce6nttvQ1vvNviypU7303u8uFLbCpOEc09cXxhHu3wYiPr", Enabled = true },
                new FinxDiscordWebhookConfig { Url = "PlaceHolder", Enabled = false },
            };

            WhitelistedWords = new List<string>
        {
        "night",
        "friendly",
    // Add more whitelisted words as needed
        };

            WhitelistedCommands = new List<string>
{
    "heal",
    "kits",
    // Add more whitelisted commands as needed
};

        }
    }



    public class FinxDiscordWebhookConfig
    {
        public string Url { get; set; }
        public bool Enabled { get; set; }
    }
}
