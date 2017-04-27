using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Transport;

namespace Aggregates
{
    public static class Bus
    {
        internal static IEndpointInstance Instance;
        internal static Func<MessageContext, Task> OnMessage;
        internal static Func<ErrorContext, Task<ErrorHandleResult>> OnError;
        internal static PushRuntimeSettings PushSettings;
        internal static bool BusOnline;

        public static async Task<IEndpointInstance> Start( EndpointConfiguration configuration)
        {
            BusOnline = false;
            var instance = await NServiceBus.Endpoint.Start(configuration).ConfigureAwait(false);
            // Take IEndpointInstance and pull out the info we need for eventstore consuming
            Instance = instance;

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
                var receivers = (
                    (IEnumerable)
                    // ReSharper disable once PossibleNullReferenceException
                    instance.GetType()
                        .GetField("receivers", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(instance)).Cast<object>();
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

                var pipelineExecutor =
                    main.GetType()
                        .GetField("pipelineExecutor", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(main);
                var recoverabilityExecutor =
                    main.GetType()
                        .GetField("recoverabilityExecutor", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(main);

                var pipelineMethod = pipelineExecutor.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
                    .MakeFuncDelegateWithTarget<MessageContext, Task>(pipelineExecutor.GetType());

                OnMessage = (c) => pipelineMethod.Invoke(pipelineExecutor, c);


                var recoverabilityMethod = recoverabilityExecutor.GetType()
                        .GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
                    .MakeFuncDelegateWithTarget<ErrorContext, Task<ErrorHandleResult>>(recoverabilityExecutor.GetType());

                OnError = (c) => recoverabilityMethod(recoverabilityExecutor, c);

                PushSettings = (PushRuntimeSettings)main.GetType()
                        .GetField("pushRuntimeSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(main);

                BusOnline = true;
                return instance;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Could not hack into NSB - event consumer won't work", e);
            }
        }
    }
}
