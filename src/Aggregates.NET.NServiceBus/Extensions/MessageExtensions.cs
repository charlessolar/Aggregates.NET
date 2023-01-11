using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Extensions
{
    [ExcludeFromCodeCoverage]
    public static class MessageExtensions
    {
        public static MessageIntent GetMessageIntent(this IIncomingLogicalMessageContext message)
        {
            var messageIntent = default(MessageIntent);

            if (message.MessageHeaders.TryGetValue(Headers.MessageIntent, out var messageIntentString))
            {
                Enum.TryParse(messageIntentString, true, out messageIntent);
            }

            return messageIntent;
        }
        public static MessageIntent GetMessageIntent(this IOutgoingLogicalMessageContext message)
        {
            var messageIntent = default(MessageIntent);

            if (message.Headers.TryGetValue(Headers.MessageIntent, out var messageIntentString))
            {
                Enum.TryParse(messageIntentString, true, out messageIntent);
            }

            return messageIntent;
        }
    }
}
