using System;
using Mirror;
using TOP.Network;
using TOP.Player;
using UnityEngine;

namespace TOP.Services
{
    public static class ChatService
    {
        public const float LocalRange = 25f;
        public const int MaxLength = 200;
        public static event Action<ChatMessage> Received;

        public static bool IsLocalRecipient(string senderMap, Vector3 senderPosition, string recipientMap, Vector3 recipientPosition)
        {
            return senderMap == recipientMap && (recipientPosition - senderPosition).sqrMagnitude <= LocalRange * LocalRange;
        }

        public static bool TrySend(string input, ChatChannel channel, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) { error = "Digite uma mensagem."; return false; }
            string text = input.Trim();
            string target = null;
            if (text.StartsWith("/invite ", StringComparison.OrdinalIgnoreCase))
            {
                var party = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerParty>() : null;
                if (party == null) { error = "Entre no jogo para convidar um jogador."; return false; }
                party.CmdPartyInvite(text.Substring(8).Trim());
                return true;
            }
            if (text.StartsWith("/g ", StringComparison.OrdinalIgnoreCase)) { channel = ChatChannel.Guild; text = text.Substring(3).Trim(); }
            else if (text.StartsWith("/p ", StringComparison.OrdinalIgnoreCase)) { channel = ChatChannel.Party; text = text.Substring(3).Trim(); }
            else if (text.StartsWith("/s ", StringComparison.OrdinalIgnoreCase)) { channel = ChatChannel.Shout; text = text.Substring(3).Trim(); }
            else if (text.StartsWith("/w ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = text.Substring(3).Trim();
                int separator = rest.IndexOf(' ');
                if (separator <= 0) { error = "Use /w nome mensagem."; return false; }
                target = rest.Substring(0, separator);
                text = rest.Substring(separator + 1).Trim();
                channel = ChatChannel.Whisper;
            }
            if (text.Length == 0 || text.Length > MaxLength)
            {
                error = $"A mensagem deve conter entre 1 e {MaxLength} caracteres.";
                return false;
            }
            if (!NetworkClient.isConnected || NetworkClient.connection == null)
            {
                error = "Chat indisponivel: sem conexao com o servidor.";
                return false;
            }
            NetworkClient.Send(new ChatMessage { Channel = channel, Text = text, TargetName = target });
            return true;
        }

        public static string Format(ChatMessage message)
        {
            string prefix = message.Channel switch
            {
                ChatChannel.Local => "[Local]", ChatChannel.World => "[Mundo]",
                ChatChannel.Party => "[Grupo]", ChatChannel.Guild => "[Guilda]",
                ChatChannel.Whisper => "[Sussurro]", ChatChannel.System => "[Sistema]",
                ChatChannel.Shout => "[Grito]", ChatChannel.Trade => "[Comercio]", _ => ""
            };
            string name = string.IsNullOrEmpty(message.SenderName) ? "" : message.SenderName + ": ";
            return prefix + " " + name + message.Text;
        }

        public static void Receive(ChatMessage message)
        {
            Received?.Invoke(message);
            if (message.Channel != ChatChannel.Local || message.SenderNetId == 0) return;
            if (NetworkClient.spawned.TryGetValue(message.SenderNetId, out var identity))
                PlayerSpeechBubble.Show(identity.gameObject, message.Text);
        }
    }
}
