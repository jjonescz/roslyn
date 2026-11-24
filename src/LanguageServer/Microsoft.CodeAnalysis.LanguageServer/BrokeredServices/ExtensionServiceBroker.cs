// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Composition;
using System.IO.Pipelines;
using Microsoft.CodeAnalysis.BrokeredServices;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.ServiceHub.Framework;

namespace Microsoft.CodeAnalysis.LanguageServer.BrokeredServices;

/// <summary>
/// The supported MEF broker contract for Roslyn-hosted extensions. It uses only ServiceHub's public
/// interface so extensions do not need to reference internal Roslyn workspace-service types.
/// </summary>
[Export("Microsoft.CodeAnalysis.LanguageServer.ServiceBroker", typeof(IServiceBroker)), Shared]
internal sealed class ExtensionServiceBroker : IServiceBroker
{
    private readonly IServiceBroker _broker;

    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public ExtensionServiceBroker(ServiceBrokerProvider provider)
        => _broker = provider.ServiceBroker;

    public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
    {
        add => _broker.AvailabilityChanged += value;
        remove => _broker.AvailabilityChanged -= value;
    }

    public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker moniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
        => _broker.GetPipeAsync(moniker, options, cancellationToken);

    public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor descriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default) where T : class
#pragma warning disable ISB001 // The caller owns the returned proxy, as on IServiceBroker.
        => _broker.GetProxyAsync<T>(descriptor, options, cancellationToken);
#pragma warning restore ISB001
}
