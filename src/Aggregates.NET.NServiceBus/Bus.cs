using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Transport;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class Bus
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Bus");

        public static IEndpointInstance Instance { get; internal set; }
        internal static Func<MessageContext, Task> OnMessage;
        internal static Func<ErrorContext, Task<ErrorHandleResult>> OnError;
        internal static PushRuntimeSettings PushSettings;
        internal static bool BusOnline;

        public static async Task<IEndpointInstance> Start(IStartableEndpointWithExternallyManagedContainer nsb)
        {
            BusOnline = false;
            Instance = await nsb.Start(new Internal.ContainerAdapter()).ConfigureAwait(false);
            // Take IEndpointInstance and pull out the info we need for eventstore consuming

            // We want eventstore to push message directly into NSB
            // That way we can have all the fun stuff like retries, error queues, incoming/outgoing mutators etc
            // But NSB doesn't have a way to just insert a message directly into the pipeline
            // You can SendLocal which will send the event out to the transport then back again but in addition to being a
            // waste of time its not safe incase this instance goes down because we'd have ACKed the event now sitting 
            // unprocessed on the instance specific queue.
            //
            // The only way I can find to call into the pipeline directly is to highjack these private fields on
            // the transport receiver.
            try
            {
                var receiveComponent = Instance.GetType()
                        .GetField("receiveComponent", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(Instance);

                var receivers = (
                    (IEnumerable)
                    // ReSharper disable once PossibleNullReferenceException
                    receiveComponent.GetType()
                        .GetField("receivers", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(receiveComponent)).Cast<object>();
                object main = null;
                foreach (var receiver in receivers)
                {
                    var id =
                        (string)
                        receiver.GetType()
                            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                            .GetValue(receiver);
                    if (id.ToLower() == "main")
                    {
                        main = receiver;
                        break;
                    }
                }
                if (main == null)
                    throw new InvalidOperationException("Could not find main receiver");

                var pipelineExecutor =
                    main.GetType()
                        .GetField("pipelineExecutor", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(main);
                var recoverabilityExecutor =
                    main.GetType()
                        .GetField("recoverabilityExecutor", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(main);

                // NSB changed these fields around 7.2 and I dont want to spend the time putting this back in (yet)
                //var tempType = pipelineExecutor
                //                    .GetType();
                //var mainPipeline = pipelineExecutor
                //                    .GetType()
                //                    .GetField("receivePipeline", BindingFlags.Instance | BindingFlags.NonPublic)
                //                    .GetValue(pipelineExecutor);
                //var behaviors = mainPipeline
                //                    .GetType()
                //                    .GetField("behaviors", BindingFlags.Instance | BindingFlags.NonPublic)
                //                    .GetValue(mainPipeline) as IBehavior[];

                var pipelineMethod = pipelineExecutor.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
                    .MakeFuncDelegateWithTarget<MessageContext, Task>(pipelineExecutor.GetType());

                OnMessage = (c) => pipelineMethod(pipelineExecutor, c);

                var recoverabilityMethod = recoverabilityExecutor.GetType()
                        .GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
                    .MakeFuncDelegateWithTarget<ErrorContext, Task<ErrorHandleResult>>(recoverabilityExecutor.GetType());

                OnError = (c) => recoverabilityMethod(recoverabilityExecutor, c);

                PushSettings = (PushRuntimeSettings)main.GetType()
                        .GetField("pushRuntimeSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(main);

                Logger.InfoEvent("Online", "NServiceBus is online");

                //for(var i = 0; i < behaviors.Length; i++)
                //    Logger.DebugEvent("PipelineStep", "{Index}: {StepType}", i, behaviors[i].GetType().FullName);

                BusOnline = true;
                return Instance;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Could not hack into NSB - event consumer won't work", e);
            }
        }
    }
}
