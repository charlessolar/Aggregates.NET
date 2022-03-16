using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
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

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            context.MessageHeaders.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}", out var corrId);

            if (string.IsNullOrEmpty(corrId))
                corrId = context.MessageId;

            var body = Encoding.UTF8.GetString(context.Message.Body);

            var properties = new Dictionary<string, object>
            {
                ["Instance"] = Defaults.Instance.ToString(),
                ["MessageId"] = context.MessageId,
                // Could body and headers be slow?
                ["MessageBody"] = body,
                ["MessageHeaders"] = new Dictionary<string, string>(context.MessageHeaders),
                ["CorrId"] = corrId,
                ["Endpoint"] = _settings.Endpoint,
                ["EndpointVersion"] = _settings.EndpointVersion.ToString(),
            };

            // Populate the logging context with useful data from the message
            using (Logger.BeginContext(properties))
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
