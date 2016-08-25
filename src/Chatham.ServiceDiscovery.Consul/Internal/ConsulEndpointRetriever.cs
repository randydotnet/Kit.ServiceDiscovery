using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Consul;

namespace Chatham.ServiceDiscovery.Consul.Internal
{
    public class ConsulEndpointRetriever : IConsulEndpointRetriever
    {
        private readonly IConsulClient _client;
        private readonly string _serviceName;
        private readonly List<string> _tags;
        private readonly bool _passingOnly;

        private ulong _waitIndex;
        private readonly CancellationToken _cancellationToken;

        public ConsulEndpointRetriever(IConsulClient client, string serviceName, List<string> tags, 
            bool passingOnly, CancellationToken cancellationToken)
        {
            _client = client;
            _serviceName = serviceName;
            _tags = tags;
            _passingOnly = passingOnly;
            _cancellationToken = cancellationToken;
        }

        public async Task<List<Uri>> FetchEndpoints()
        {
            // Consul doesn't support more than one tag in its service query method.
            // https://github.com/hashicorp/consul/issues/294
            // Hashicorp suggest prepared queries, but they don't support blocking.
            // https://www.consul.io/docs/agent/http/query.html#execute
            // If we want blocking for efficiency, we must filter tags manually.
            var tag = string.Empty;
            if (_tags.Count > 0)
            {
                tag = _tags[0];
            }

            var queryOptions = new QueryOptions
            {
                WaitIndex = _waitIndex
            };
            var servicesTask = await _client.Health.Service(_serviceName, tag, _passingOnly, queryOptions, _cancellationToken);

            if (_tags.Count > 1)
            {
                servicesTask.Response = FilterByTag(servicesTask.Response, _tags);
            }

            _waitIndex = servicesTask.LastIndex;

            return CreateEndpointUris(servicesTask.Response);
        }

        private static List<Uri> CreateEndpointUris(ServiceEntry[] services)
        {
            var serviceUris = new List<Uri>();
            foreach (var service in services)
            {
                var host = !string.IsNullOrWhiteSpace(service.Service.Address)
                    ? service.Service.Address
                    : service.Node.Address;
                var builder = new UriBuilder("http", host, service.Service.Port);
                serviceUris.Add(builder.Uri);
            }
            return serviceUris;
        }

        private static ServiceEntry[] FilterByTag(ServiceEntry[] entries, List<string> tags)
        {
            return entries
                .Where(x => tags.All(x.Service.Tags.Contains))
                .ToArray();
        }
    }
}