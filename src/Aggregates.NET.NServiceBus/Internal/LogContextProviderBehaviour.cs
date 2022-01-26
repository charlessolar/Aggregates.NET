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
    internal class LogContextProviderBehaviour : Behavior<IIncomingLogicalMessageContext>
    {
        private readonly ILogger Logger;

        private readonly Configure _settings;

        public LogContextProviderBehaviour(ILoggerFactory logFactory, Configure settings)
        {
            Logger = logFactory.CreateLogger("LogContextProvider");
            _settings = settings;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            // Populate the logging context with useful data from the messaeg
            using (Logger.BeginContext("Instance", Defaults.Instance.ToString()))
            {
                string messageId = "";
                context.MessageHeaders.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}", out messageId);

                using (Logger.BeginContext("MessageId", messageId))
                {
                    string corrId = "";
                    context.MessageHeaders.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}", out corrId);

                    if (string.IsNullOrEmpty(corrId))
                        corrId = messageId;
                    using (Logger.BeginContext("CorrId", corrId))
                    {
                        using (Logger.BeginContext("Endpoint", _settings.Endpoint))
                        {
                            Logger.DebugEvent("Start", "Processing [{MessageId:l}] Corr: [{CorrelationId:l}]", messageId, corrId);
                            await next().ConfigureAwait(false);
                            Logger.DebugEvent("End", "Processing [{MessageId:l}] Corr: [{CorrelationId:l}]", messageId, corrId);
                        }
                    }
                }
            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class LogContextProviderRegistration : RegisterStep
    {
        public LogContextProviderRegistration() : base(
            stepId: "LogContextProvider",
            behavior: typeof(LogContextProviderBehaviour),
            description: "Provides useful message information to logger")
        {
            InsertAfter("LocalMessageUnpack");
        }
    }
}
