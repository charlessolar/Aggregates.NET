using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    internal class LogContextProviderBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private readonly ILogger Logger;

        private readonly ISettings _settings;

        public LogContextProviderBehavior(ILogger<LogContextProviderBehavior> logger, ISettings settings)
        {
            Logger = logger;
            _settings = settings;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next) {
			if(!context.MessageHeaders.TryGetValue($"{NSBDefaults.CorrelationIdHeader}", out var corrId))
			    context.MessageHeaders.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}", out corrId);
            context.MessageHeaders.TryGetValue($"{NServiceBus.Headers.EnclosedMessageTypes}", out var messageTypes);

            if (string.IsNullOrEmpty(corrId))
                corrId = context.MessageId;


            var properties = new Dictionary<string, object>
            {
                ["MessageId"] = context.MessageId,
                ["MessageType"] = messageTypes,
                ["CorrelationId"] = corrId,
                ["Endpoint"] = _settings.Endpoint,
				["EndpointInstance"] = Defaults.Instance.ToString(),
				["EndpointVersion"] = _settings.EndpointVersion.ToString(),
            };

            // Populate the logging context with useful data from the message
            using (Logger.BeginScope(properties))
            {
                Logger.DebugEvent("Start", "Started processing [{MessageId:l}] Corr: [{CorrelationId:l}]", context.MessageId, corrId);
                await next().ConfigureAwait(false);
                Logger.DebugEvent("End", "Finished processing [{MessageId:l}] Corr: [{CorrelationId:l}]", context.MessageId, corrId);
            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class LogContextProviderRegistration : RegisterStep
    {
        public LogContextProviderRegistration() : base(
            stepId: "LogContextProvider",
            behavior: typeof(LogContextProviderBehavior),
            description: "Provides useful scope information to logger")
        {
            InsertBefore("MutateIncomingTransportMessage");
        }
    }
}
