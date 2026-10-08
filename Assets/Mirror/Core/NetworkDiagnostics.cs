using System;

namespace Mirror
{
    /// <summary>Profiling statistics for tool to subscribe to (profiler etc.)</summary>
    public static class NetworkDiagnostics
    {
        /// <summary>Describes an outgoing message</summary>
        public readonly struct MessageInfo
        {
            /// <summary>The message being sent</summary>
            public readonly NetworkMessage message;
            /// <summary>channel through which the message was sent</summary>
            public readonly int channel;
            /// <summary>how big was the message (does not include transport headers)</summary>
            public readonly int bytes;
            /// <summary>How many connections was the message sent to.</summary>
            public readonly int count;

            internal MessageInfo(NetworkMessage message, int channel, int bytes, int count)
            {
                this.message = message;
                this.channel = channel;
                this.bytes = bytes;
                this.count = count;
            }
        }

        /// <summary>Event for when Mirror sends a message. Can be subscribed to.</summary>
        public static event Action<MessageInfo> OutMessageEvent;

        /// <summary>Event for when Mirror receives a message. Can be subscribed to.</summary>
        public static event Action<MessageInfo> InMessageEvent;

        // TOP: time spent inside each message handler (GameTrace); only measured while subscribed.
        public static event Action<Type, double> HandlerEvent;
        internal static bool HandlerTiming => HandlerEvent != null;

        internal static void OnHandled(Type type, long startedTimestamp)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startedTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            HandlerEvent?.Invoke(type, ms);
        }

        // RuntimeInitializeOnLoadMethod -> fast playmode without domain reload
        [UnityEngine.RuntimeInitializeOnLoadMethod]
        static void ResetStatics()
        {
            InMessageEvent = null;
            OutMessageEvent = null;
            HandlerEvent = null;
        }

        internal static void OnSend<T>(T message, int channel, int bytes, int count)
            where T : struct, NetworkMessage
        {
            if (count > 0 && OutMessageEvent != null)
            {
                MessageInfo outMessage = new MessageInfo(message, channel, bytes, count);
                OutMessageEvent?.Invoke(outMessage);
            }
        }

        internal static void OnReceive<T>(T message, int channel, int bytes)
            where T : struct, NetworkMessage
        {
            if (InMessageEvent != null)
            {
                MessageInfo inMessage = new MessageInfo(message, channel, bytes, 1);
                InMessageEvent?.Invoke(inMessage);
            }
        }
    }
}
