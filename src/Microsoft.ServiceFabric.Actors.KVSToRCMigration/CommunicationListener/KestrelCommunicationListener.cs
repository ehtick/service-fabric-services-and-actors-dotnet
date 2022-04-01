// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.ServiceFabric.Actors.KVSToRCMigration
{
    using System;
    using System.Fabric;
    using System.Globalization;
    using Microsoft.AspNetCore.Hosting;

    /// <summary>
    /// An AspNetCore Kestrel based communication listener for Service Fabric stateless or stateful service.
    /// </summary>
    internal class KestrelCommunicationListener : AspNetCoreCommunicationListener
    {
        private readonly string endpointName;

        /// <summary>
        /// Initializes a new instance of the <see cref="KestrelCommunicationListener"/> class.
        /// </summary>
        /// <param name="serviceContext">The context of the service for which this communication listener is being constructed.</param>
        /// <param name="endpointName">Name of endpoint resource defined in service manifest that should be used to create the address for listener.
        /// Protocol and port specified in this endpoint is used to create the url.
        /// If the endpointName is null, a default address with http protocol and port 0 will be used.
        /// Kestrel will dynamically bind to an unspecified, available port when port 0 is specified in url.
        /// If the specified endpointName is not found in service manifest, an InvalidOperationException indicating this will be thrown.</param>
        /// <param name="build">Delegate to build Microsoft.AspNetCore.Hosting.IWebHost, endpoint url generated by the listener is given as input to this delegate.
        /// This gives the flexibility to change the url before creating Microsoft.AspNetCore.Hosting.IWebHost if needed.</param>
        public KestrelCommunicationListener(ServiceContext serviceContext, string endpointName, Func<string, AspNetCoreCommunicationListener, IWebHost> build)
            : base(serviceContext, build)
        {
            if (endpointName?.Length == 0)
            {
                throw new ArgumentException("endpointResourceName cannot be empty string.");
            }

            this.endpointName = endpointName;
        }

        /// <summary>
        /// Gets url for the listener. Listener url is created using the endpointName passed in the constructor.
        /// If the endpointName was null, a default url with http protocol and port zero is returned.
        /// </summary>
        /// <returns>url for the listener.</returns>
        protected internal override string GetListenerUrl()
        {
            // url with WebServer is always registered as http://+:port.
            var listenUrl = "http://+:0";

            // Get protocol and port from endpoint resource if specified.
            if (this.endpointName != null)
            {
                var serviceEndpoint = this.GetEndpointResourceDescription(this.endpointName);
                listenUrl = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}://+:{1}",
                    serviceEndpoint.Protocol.ToString().ToLowerInvariant(),
                    serviceEndpoint.Port);
            }

            return listenUrl;
        }
    }
}
