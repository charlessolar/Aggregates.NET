using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Aggregates
{
    public class Configuration : IConfiguration
    {

        public IServiceProvider ServiceProvider { get; internal set; }

        public bool Setup => Settings != null;
        public ISettings Settings { get; internal set; }

        public async Task Start(IServiceProvider serviceProvider)
        {
            if (Settings == null)
                throw new InvalidOperationException("Settings must be built");

            ServiceProvider = serviceProvider;

            try
            {
                var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
                using (var scope = scopeFactory.CreateScope())
                {
                    await Internal.Settings.BusTasks.WhenAllAsync(x => x(serviceProvider, Settings)).ConfigureAwait(false);
                    await Internal.Settings.StartupTasks.WhenAllAsync(x => x(serviceProvider, Settings)).ConfigureAwait(false);


                    IUnitOfWork uow = null;
                    try
                    {
                        // verify certain agg.net stuff now we have a container
                        uow = scope.ServiceProvider.GetService<UnitOfWork.IUnitOfWork>();
                    } catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to create IUnitOfWork object, something might be wrong with your Aggregates constructor implementation", ex);
                    }
                    // i didnt want to make this interface explicit to avoid the user being able to do `ctx.Uow().End()` in his handlers like a silly
                    if (uow != null && !(uow is UnitOfWork.IBaseUnitOfWork))
                        throw new InvalidOperationException($"Unit of work {uow.GetType().Name} needs to also implement {typeof(UnitOfWork.IBaseUnitOfWork)}");
                }
            } catch
            {
                Settings = null;
                throw;
            }
        }
        public Task Stop(IServiceProvider serviceProvider)
        {
            if (Settings == null)
                throw new InvalidOperationException("Settings not set, aggregates.net not started?");
            return Internal.Settings.ShutdownTasks.WhenAllAsync(x => x(serviceProvider, Settings));
        }


        public async static Task<IConfiguration> Build(IServiceCollection serviceCollection, Action<Settings> settings)
        {
            if (serviceCollection == null)
                throw new ArgumentException("Must designate the service collection");

            var config = new Settings();
            var aggConfig = new Configuration();

            aggConfig.Settings = config;
            config.Configuration = aggConfig;

            settings(config);

            try
            {
                serviceCollection.AddSingleton<ISettings>(config);
                serviceCollection.AddSingleton<IConfiguration>(aggConfig);
                await Internal.Settings.RegistrationTasks.WhenAllAsync(x => x(serviceCollection, config)).ConfigureAwait(false);
            } catch
            {
                throw;
            }
            return aggConfig;
        }
    }
}
