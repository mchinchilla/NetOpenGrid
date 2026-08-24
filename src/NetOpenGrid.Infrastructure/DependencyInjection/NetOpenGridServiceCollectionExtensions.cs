using Microsoft.Extensions.DependencyInjection;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Infrastructure.Runtime;

namespace NetOpenGrid.Infrastructure;

/// <summary>Composition-root entry point for registering grids.</summary>
public static class NetOpenGridServiceCollectionExtensions
{
    public static NetOpenGridBuilder AddNetOpenGrid(this IServiceCollection services)
    {
        return services.AddNetOpenGrid(_ => { }, _ => { });
    }

    public static NetOpenGridBuilder AddNetOpenGrid(this IServiceCollection services, Action<NetOpenGridAssetOptions> configure)
    {
        return services.AddNetOpenGrid(configure, _ => { });
    }

    public static NetOpenGridBuilder AddNetOpenGrid(
        this IServiceCollection services,
        Action<NetOpenGridAssetOptions>? configure,
        Action<NetOpenGridLocalizationOptions>? localize)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assetOptions = new NetOpenGridAssetOptions();
        configure?.Invoke(assetOptions);
        services.AddSingleton(assetOptions);

        var localizationOptions = new NetOpenGridLocalizationOptions();
        localize?.Invoke(localizationOptions);
        services.AddSingleton(localizationOptions);

        return new NetOpenGridBuilder(services);
    }
}

public sealed class NetOpenGridBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Registers a grid keyed by <paramref name="id"/>. Options are built and validated once,
    /// eagerly, at registration time.
    /// </summary>
    public NetOpenGridBuilder AddGrid<TItem>(
        string id,
        Action<GridOptionsBuilder<TItem>> configure,
        Func<IServiceProvider, GridOptions<TItem>, IGridDataSource<TItem>> dataSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(dataSourceFactory);

        var optionsBuilder = new GridOptionsBuilder<TItem>().WithId(id);
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        return Register(options, dataSourceFactory);
    }

    /// <summary>Registers a grid from pre-built (already validated) options.</summary>
    public NetOpenGridBuilder AddGrid<TItem>(
        GridOptions<TItem> options,
        Func<IServiceProvider, GridOptions<TItem>, IGridDataSource<TItem>> dataSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSourceFactory);
        return Register(options, dataSourceFactory);
    }

    private NetOpenGridBuilder Register<TItem>(
        GridOptions<TItem> options,
        Func<IServiceProvider, GridOptions<TItem>, IGridDataSource<TItem>> dataSourceFactory)
    {
        Services.AddKeyedSingleton<IGridRuntime>(
            options.Id,
            (serviceProvider, _) => new GridRuntime<TItem>(
                options,
                dataSourceFactory(serviceProvider, options),
                serviceProvider.GetRequiredService<NetOpenGridAssetOptions>(),
                serviceProvider.GetRequiredService<NetOpenGridLocalizationOptions>()));

        return this;
    }
}
