using Rocket.API;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Http;
using Steamworks;
using System.Linq;
using System;
using Rocket.Core.Commands;
using static UnityEngine.GraphicsBuffer;
using Rocket.Core;
using static System.Net.WebRequestMethods;

namespace FinxSpamChatFilter
{
    public class FinxSpamChatFilter : RocketPlugin<FinxSpamChatFilterConfig>
    {
        public static FinxSpamChatFilter Instance;
        private FinxDiscordWebhookHandler discordWebhookHandler;

        private Dictionary<CSteamID, int> WarnedPlayers = new Dictionary<CSteamID, int>();
        private Dictionary<CSteamID, Queue<float>> LastMessageTimes = new Dictionary<CSteamID, Queue<float>>();
        private Dictionary<CSteamID, List<string>> CollectedMessages = new Dictionary<CSteamID, List<string>>();
        private Dictionary<CSteamID, float> MuteStartTime = new Dictionary<CSteamID, float>();
        private Dictionary<CSteamID, Tuple<MuteReason, int>> MutedPlayers = new Dictionary<CSteamID, Tuple<MuteReason, int>>();
        private HashSet<CSteamID> DelayedMuteMessages = new HashSet<CSteamID>();
        private Dictionary<CSteamID, List<string>> CollectedBadWordMessages = new Dictionary<CSteamID, List<string>>();
        private Dictionary<CSteamID, float> LastBadWordMessageTime = new Dictionary<CSteamID, float>();
        private Dictionary<CSteamID, bool> MutedForSpam = new Dictionary<CSteamID, bool>();
        private Dictionary<CSteamID, DateTime> MuteEndTimes = new Dictionary<CSteamID, DateTime>();
        private List<string> MessagesForPunishment = new List<string>();
        

        

        

        

        

        public delegate void ClientUnityEventPermissionsHandler(SteamPlayer player, string command, ref bool shouldExecuteCommand, ref bool shouldList);


        public override TranslationList DefaultTranslations => new TranslationList
{
    {"mute_broadcast_chat_filter", "{0} is now muted for {1} seconds by AutoMod for inappropriate language."},
    {"unmute_broadcast_chat_filter", "{0} is now unmuted by AutoMod for inappropriate language."},
    {"mute_broadcast_chat_spam", "{0} is now muted for {1} seconds by AutoMod for excessive chat spam."},
    {"unmute_broadcast_chat_spam", "{0} is now unmuted by AutoMod for excessive chat spam."},
    {"remaining_mute_time", "You are still muted for {1} seconds."},
    {"warning_message_chat_filter", "You used inappropriate language! Warning {0}/{1}."},
    {"chat_mute_message", "You have been muted for {1} seconds."},
    {"spam_mute_message", "You have been muted for {1} seconds due to excessive chat spam."},
    {"unmute_message_chat_filter", "You have been unmuted for inappropriate language."},
    {"unmute_message_chat_spam", "You have been unmuted for excessive chat spam."},
    {"force_unmute", "You have been Forcefully Unmuted!"},
    {"muted_player_blacklisted_command", "You cannot execute this command while muted!"}
};

        protected override void Load()
        {
            Instance = this;
            U.Events.OnPlayerConnected += OnPlayerConnected;
            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerChatted += PlayerChatted;
            R.Commands.OnExecuteCommand += CommandExecuted;
            discordWebhookHandler = new FinxDiscordWebhookHandler(Configuration.Instance.DiscordWebhooks);
            Rocket.Core.Logging.Logger.Log("Contact finx1 on discord to report bugs!", ConsoleColor.Yellow);
        }

        protected override void Unload()
        {
            U.Events.OnPlayerConnected -= OnPlayerConnected;
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            UnturnedPlayerEvents.OnPlayerChatted -= PlayerChatted;
            R.Commands.OnExecuteCommand -= CommandExecuted;
            discordWebhookHandler.Dispose();
        }

        private void OnPlayerConnected(UnturnedPlayer player)
        {
            LastMessageTimes.Add(player.CSteamID, new Queue<float>());
            CollectedMessages.Add(player.CSteamID, new List<string>());

           

        }


        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            WarnedPlayers.Remove(player.CSteamID);
            LastMessageTimes.Remove(player.CSteamID);
            CollectedMessages.Remove(player.CSteamID);
        }

        private void MuteChatFilterPlayer(UnturnedPlayer player, int duration, string reason)
        {
            if (!MutedPlayers.ContainsKey(player.CSteamID) && Configuration.Instance.PunishmentsEnabled)
            {
                MutedPlayers.Add(player.CSteamID, new Tuple<MuteReason, int>(MuteReason.ChatFilter, duration));
                MuteStartTime[player.CSteamID] = Time.realtimeSinceStartup;
                MuteEndTimes[player.CSteamID] = DateTime.UtcNow.AddSeconds(duration);

                Invoke(nameof(UnmuteChatFilterPlayer), Configuration.Instance.AutoMuteDuration);

                if (Configuration.Instance.EnableMuteWorldBroadcasts)
                {
                    UnturnedChat.Say(Translate("mute_broadcast_chat_filter", player.DisplayName, duration));
                }

                int remainingMuteTime = (int)Mathf.Ceil(GetUnmuteTime(player));
                string muteTimeMessage = Translate("chat_mute_message", player.DisplayName, remainingMuteTime);
                ChatManager.serverSendMessage(muteTimeMessage, Color.red, null, player.SteamPlayer(), EChatMode.GLOBAL, null, true);

            }
        }
        

        private void MuteChatSpamPlayer(UnturnedPlayer player, int duration, string reason)
        {
            if (!MutedPlayers.ContainsKey(player.CSteamID))
            {
                MutedPlayers[player.CSteamID] = new Tuple<MuteReason, int>(MuteReason.ChatSpam, duration);
                MuteStartTime[player.CSteamID] = Time.realtimeSinceStartup;
                MuteEndTimes[player.CSteamID] = DateTime.UtcNow.AddSeconds(duration);

                MutedForSpam[player.CSteamID] = true;

                if (Configuration.Instance.EnableMuteWorldBroadcasts)
                {
                    UnturnedChat.Say(Translate("mute_broadcast_chat_spam", player.DisplayName, duration));
                }

                string muteTimeMessage = Translate("spam_mute_message", player.DisplayName, duration);
                UnturnedChat.Say(player, muteTimeMessage);

                Invoke(nameof(UnmuteChatSpamPlayer), duration);
            }
        }

        private void UnmuteChatFilterPlayer()
        {
            var playersToUnmute = new List<CSteamID>(MutedPlayers.Keys);
            foreach (var playerID in playersToUnmute)
            {
                UnturnedPlayer player = UnturnedPlayer.FromCSteamID(playerID);
                if (player != null)
                {
                    MutedPlayers.Remove(playerID);
                    MuteEndTimes.Remove(playerID);

                    if (Configuration.Instance.EnableMuteWorldBroadcasts)
                    {
                        string unmuteMessage = Translate("unmute_broadcast_chat_filter", player.DisplayName);
                        UnturnedChat.Say(unmuteMessage, Color.green);
                    }
                    string privateUnmuteMessage = Translate("unmute_message_chat_filter");
                    UnturnedChat.Say(player, privateUnmuteMessage, Color.green);
                }
            }
        }

        private void UnmuteChatSpamPlayer()
        {
            var playersToUnmute = new List<CSteamID>(MutedPlayers.Keys);
            foreach (var playerID in playersToUnmute)
            {
                UnturnedPlayer player = UnturnedPlayer.FromCSteamID(playerID);
                if (player != null)
                {
                    MutedPlayers.Remove(playerID);
                    MutedForSpam.Remove(playerID);

                    if (Configuration.Instance.EnableMuteWorldBroadcasts)
                    {
                        string unmuteMessage = Translate("unmute_broadcast_chat_spam", player.DisplayName);
                        UnturnedChat.Say(unmuteMessage, Color.green);
                    }
                    string privateUnmuteMessage = Translate("unmute_message_chat_spam");
                    UnturnedChat.Say(player, privateUnmuteMessage, Color.green);
                }
            }
        }

        private void CheckChatFilter(UnturnedPlayer player, ref Color color, string message, EChatMode chatMode, ref bool cancel)
        {
            int maxWarnings = Configuration.Instance.MaxWarnings;

            if (!player.HasPermission("bypass.chatfilter"))
            {
                List<string> words = message.ToLower().Split(' ').ToList();

                foreach (string whitelistedWord in Configuration.Instance.WhitelistedWords)
                {
                    if (words.Contains(whitelistedWord.ToLower()))
                    {
                        words.RemoveAll(word => word == whitelistedWord.ToLower());
                    }
                }

                foreach (string badWord in Configuration.Instance.BadWords)
                {
                    if (words.Any(word => word.Contains(badWord.ToLower())))
                    {
                        if (Configuration.Instance.PunishmentsEnabled)
                        {
                            if (WarnedPlayers.TryGetValue(player.CSteamID, out int currentWarnings))
                            {
                                currentWarnings++;
                                WarnedPlayers[player.CSteamID] = currentWarnings;

                                if (currentWarnings >= maxWarnings)
                                {
                                    MuteChatFilterPlayer(player, Configuration.Instance.AutoMuteDuration, "Too many warnings for bad language.");
                                    WarnedPlayers.Remove(player.CSteamID);
                                }
                                else
                                {
                                    UnturnedChat.Say(player, Translate("warning_message_chat_filter", currentWarnings, maxWarnings));
                                }
                            }
                            else
                            {
                                WarnedPlayers.Add(player.CSteamID, 1);
                                UnturnedChat.Say(player, Translate("warning_message_chat_filter", 1, maxWarnings));
                            }
                        }

                        // Send to webhook handler for messages that should have resulted in a mute, regardless of punishments
                        SendToWebhookHandler(player, message, true);

                        if (Configuration.Instance.PunishmentsEnabled)
                        {
                            cancel = true; // Cancel the message only when punishments are enabled
                        }
                        return;
                    }
                }

                // Send to webhook handler for messages that should have resulted in a mute, regardless of punishments
                SendToWebhookHandler(player, message, false);
            }
        }

        

        private bool IsWhitelistedWord(string message, List<string> whitelistedWordsInMessage)
{
    foreach (string whitelistedWord in Configuration.Instance.WhitelistedWords)
    {
        if (message.Contains(whitelistedWord.ToLower()))
        {
            whitelistedWordsInMessage.Add(whitelistedWord.ToLower());

            foreach (string badWord in Configuration.Instance.BadWords)
            {
                if (!whitelistedWordsInMessage.Contains(badWord.ToLower()) && whitelistedWord.ToLower().Contains(badWord.ToLower()))
                {
                    return true; // The whitelisted word contains a blacklisted substring
                }
            }

            return false; // The word is whitelisted
        }
    }
    return false; // The word is not whitelisted
}




        private float GetUnmuteTime(UnturnedPlayer player)
        {
            if (MuteEndTimes.TryGetValue(player.CSteamID, out DateTime muteEndTime))
            {
                return (float)Math.Max(0, (muteEndTime - DateTime.UtcNow).TotalSeconds);
            }
            return 0f;
        }

        private void CheckAntiSpam(UnturnedPlayer player, ref Color color, string message, EChatMode chatMode, ref bool cancelSpam)
        {
            if (!player.HasPermission("bypass.antispam"))
            {
                float currentTime = Time.realtimeSinceStartup;
                Queue<float> messageTimes = LastMessageTimes[player.CSteamID];

                while (messageTimes.Count > 0 && currentTime - messageTimes.Peek() > Configuration.Instance.MaxMessagesDuration)
                {
                    messageTimes.Dequeue();
                    CollectedMessages[player.CSteamID]?.Clear();
                }

                if (messageTimes.Count >= Configuration.Instance.MaxMessagesPerDuration)
                {
                    if (Configuration.Instance.PunishmentsEnabled)
                    {
                        MuteChatSpamPlayer(player, Configuration.Instance.SpamMuteDuration, "Exceeded message limit per duration");
                    }
                    cancelSpam = true;
                }

                messageTimes.Enqueue(currentTime);
                CollectedMessages[player.CSteamID].Add(player.SteamPlayer().playerID.characterName + ": " + message);

                int maxSpammedMessages = Configuration.Instance.MaxMessagesPerDuration;

                if (CollectedMessages[player.CSteamID].Count >= maxSpammedMessages)
                {
                    var collectedMessages = CollectedMessages[player.CSteamID].ToList();
                    collectedMessages.Insert(0, "Steam ID: " + player.CSteamID);
                    discordWebhookHandler.EnqueueMessages(collectedMessages);
                    CollectedMessages[player.CSteamID].Clear();

                    if (Configuration.Instance.PunishmentsEnabled)
                    {
                        cancelSpam = true; // Cancel the message only when punishments are enabled
                    }
                }
                else
                {
                    float timeUntilCheck = Configuration.Instance.MaxMessagesDuration - (currentTime - messageTimes.Peek());
                    Invoke(nameof(CheckCollectedMessages), timeUntilCheck);

                    if (!MutedPlayers.ContainsKey(player.CSteamID) && MutedForSpam.ContainsKey(player.CSteamID))
                    {
                        int remainingMuteTime = (int)Mathf.Ceil(GetUnmuteTime(player));
                        string muteTimeMessage = Translate("spam_mute_message", player.DisplayName, remainingMuteTime);
                        ChatManager.serverSendMessage(muteTimeMessage, Color.red, null, player.SteamPlayer(), EChatMode.GLOBAL, null, true);
                    }
                }

                // Send to webhook handler for messages that should have resulted in a mute, regardless of punishments
                if (cancelSpam)
                {
                    SendToWebhookHandler(player, message, true);
                }
                else
                {
                    SendToWebhookHandler(player, message, false);
                }
            }
        }

        
        

        private List<string> CreateWebhookMessages(UnturnedPlayer player, string message)
        {
            return new List<string>
            {
                $"Player: {player.DisplayName}",
                $"Steam ID: {player.CSteamID}",
                $"Message: {message}"
            };
        }

        private void SendToWebhookHandler(UnturnedPlayer player, string message, bool isSpamOrBlacklisted)
        {
            if (isSpamOrBlacklisted)
            {
                var webhookMessages = CreateWebhookMessages(player, message);
                discordWebhookHandler.EnqueueMessages(webhookMessages);
            }
        }


        private void CheckCollectedMessages()
        {
            foreach (var playerID in CollectedMessages.Keys.ToList())
            {
                if (CollectedMessages.TryGetValue(playerID, out List<string> messagesBuffer))
                {
                    int maxSpammedMessages = Configuration.Instance.MaxMessagesPerDuration;

                    if (messagesBuffer.Count >= maxSpammedMessages)
                    {
                        var messages = messagesBuffer;
                        discordWebhookHandler.EnqueueMessages(new List<string>
                {
                    string.Join("\n", messages)
                });

                        messages.Clear();
                    }
                }
            }
        }

       

        private float GetTimeSinceLastBadWordMessage(UnturnedPlayer player)
        {
            if (LastBadWordMessageTime.TryGetValue(player.CSteamID, out float lastMessageTime))
            {
                return Time.realtimeSinceStartup - lastMessageTime;
            }
            return float.MaxValue;
        }

       
        private void SendCollectedBadWordMessages(UnturnedPlayer player)
        {
            if (CollectedBadWordMessages.TryGetValue(player.CSteamID, out List<string> collectedMessages))
            {
                if (collectedMessages.Count > 0)
                {
                    var messages = new List<string>();
                    messages.Add($"Steam ID: {player.CSteamID}");

                    foreach (var message in collectedMessages)
                    {
                        messages.Add($"{player.DisplayName}: {message}");
                    }

                    discordWebhookHandler.EnqueueMessages(messages);
                    collectedMessages.Clear();
                }
            }
        }

        [RocketCommand("finxunmute", "Unmute a player", "<player>", AllowedCaller.Player)]
        public void UnmutePlayer(IRocketPlayer caller, string[] args)
        {
            if (args.Length != 1)
            {
                UnturnedChat.Say(caller, "Usage: /unmute <player>", Color.red);
                return;
            }

            string targetPlayerName = args[0];
            UnturnedPlayer targetPlayer = UnturnedPlayer.FromName(targetPlayerName);

            if (targetPlayer == null)
            {
                UnturnedChat.Say(caller, "Player not found.", Color.red);
                return;
            }

            if (MutedPlayers.ContainsKey(targetPlayer.CSteamID))
            {
                MutedPlayers.Remove(targetPlayer.CSteamID);
                MutedForSpam.Remove(targetPlayer.CSteamID);
                MuteEndTimes.Remove(targetPlayer.CSteamID);

                string privateUnmuteMessage = Translate("force_unmute");
                UnturnedChat.Say(targetPlayer, privateUnmuteMessage, Color.green);

                UnturnedChat.Say(caller, $"{targetPlayer.DisplayName} has been unmuted.", Color.green);
            }
            else
            {
                UnturnedChat.Say(caller, $"{targetPlayer.DisplayName} is not muted.", Color.red);
            }
        }

        
        private void CommandExecuted(IRocketPlayer player, IRocketCommand command, ref bool cancel)
        {
            if (player is UnturnedPlayer unturnedPlayer)
            {
                if (MutedPlayers.ContainsKey(unturnedPlayer.CSteamID))
                {
                    // Player is muted, check if the executed command is blacklisted
                    if (IsBlacklistedCommand(command.Name))
                    {
                        // Player is trying to execute a blacklisted command, cancel the command execution
                        UnturnedChat.Say(unturnedPlayer, Translate("muted_player_blacklisted_command"), Color.red);
                        cancel = true;
                        return;
                    }
                }
            }
        }

        // Define a method to check if a command is blacklisted
        private bool IsBlacklistedCommand(string commandName)
        {
            // Load the list of blacklisted commands from your configuration
            List<string> blacklistedCommands = Configuration.Instance.BlacklistedCommands;

            // Check if the command name is in the list of blacklisted commands
            return blacklistedCommands.Contains(commandName, StringComparer.OrdinalIgnoreCase);
        }



      
        private void PlayerChatted(UnturnedPlayer player, ref Color color, string message, EChatMode chatMode, ref bool cancel)
        {

            
            if (MutedPlayers.ContainsKey(player.CSteamID))
            {
                // Check if the message starts with "/"
                if (!message.StartsWith("/"))
                {
                    int remainingMuteTime = (int)Mathf.Ceil(GetUnmuteTime(player));
                    string muteTimeMessage = Translate("remaining_mute_time", player.DisplayName, remainingMuteTime);
                    UnturnedChat.Say(player, muteTimeMessage);

                    // Cancel the normal chat message
                    cancel = true;
                }
                else
                {
                    // If the message starts with "/", don't send the "remaining mute time" message
                    // and don't cancel the normal chat message
                    cancel = false;
                }

                return;
            }

            // Check if the message is a command
            if (message.StartsWith("/"))
            {
                string commandName = message.Split(' ')[0].Substring(1).ToLower(); // Extract the command name

                // Check if the executed command is in the list of whitelisted commands
                if (Configuration.Instance.WhitelistedCommands.Contains(commandName))
                {
                    // The command is whitelisted, so don't perform spam or chat filter checks
                    return;
                }
            }

            CheckChatFilter(player, ref color, message, chatMode, ref cancel);

            if (!cancel)
            {
                CheckAntiSpam(player, ref color, message, chatMode, ref cancel);
            }
        }

    }

}













