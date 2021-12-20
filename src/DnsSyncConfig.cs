using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dns_sync
{
    internal class DnsSyncConfig
    {
        public static DnsSyncConfig LoadAndValidate(string path)
        {
            DnsSyncLogger.LogInformation($"Loading config from: {path}");

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var ymlText = System.IO.File.ReadAllText(path);

                var config = deserializer.Deserialize<DnsSyncConfig>(ymlText);

                DnsSyncLogger.LogInformation($"Correctly Loaded config from: {path}");

                config.ThrowIfConfigIsInvalid();

                return config;
            }
            catch (Exception e)
            {
                DnsSyncLogger.LogError("There was an error while loading the config", e);
                throw;
            }
        }

        public void ThrowIfConfigIsInvalid()
        {
            if (this.Hosts == null)
            {
                throw new Exception("Config is missing 'hosts' section");
            }
            if (this.Hosts.Count == 0)
            {
                throw new Exception("Config does not include any hosts");
            }

            foreach (var h in this.Hosts)
            {
                h.ThrowIfConfigIsInvalid();
            }

            if (this.Dnsmasq != null)
            {
                this.Dnsmasq.ThrowIfConfigIsInvalid();
            }

            if (this.Auth != null)
            {
                this.Auth.ThrowIfConfigIsInvalid();
            }

            if (ScanFrequency <= 0)
            {
                throw new Exception("scan_frequency must be greater than 1 second");
            }
        }

        public string Save()
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            return serializer.Serialize(this);
        }

        public int ScanFrequency { get; set; }

        public LogLevel? LogLevel { get; set; }

        public IList<DockerHostConfig>? Hosts { get; set; }

        public DnsmasqConfig? Dnsmasq { get; set; }

        public DockerAuth? Auth { get; set; }
    }

    internal class DnsmasqConfig
    {
        public DnsmasqConfig()
        {
            TargetFile = "";
        }

        public string? HostUri { get; set; }
        public string? ContainerName { get; set; }
        public string TargetFile { get; set; }
        public void ThrowIfConfigIsInvalid()
        {
            if (this.HostUri != null && string.IsNullOrWhiteSpace(this.HostUri))
            {
                throw new Exception($"Invalid URI provided for dnsmasq docker host: {this.HostUri}");
            }

            if (this.ContainerName != null && string.IsNullOrWhiteSpace(this.ContainerName))
            {
                throw new Exception($"Invalid name provided for dnsmasq docker container: {this.HostUri}");
            }

            if (string.IsNullOrWhiteSpace(this.TargetFile))
            {
                throw new Exception("Config is missing 'dnsmasq:target_file' section");
            }

            System.IO.File.AppendAllText(this.TargetFile, "");
        }
    }

    internal class DockerHostConfig
    {
        public DockerHostConfig()
        {
            Uri = "";
        }

        public string Uri { get; set; }
        public string? Hostname { get; set; }

        public void ThrowIfConfigIsInvalid()
        {
            if (string.IsNullOrWhiteSpace(this.Uri))
            {
                throw new Exception($"Invalid URI provided for docker host: {this.Uri}");
            }

            if (Hostname != null && string.IsNullOrWhiteSpace(Hostname))
            {
                throw new Exception($"Invalid Hostname Override provided for docker host: {this.Uri}");
            }

            var hostUri = new Uri(this.Uri);

            if (Hostname == null && hostUri.Scheme == "unix")
            {
                throw new Exception($"Cannot infer a hostname from a Unix Socket connection: {this.Uri}");
            }
        }
    }

    internal class DockerAuth
    {
        public DockerMutualTLSAuth? MutualTls { get; set; }

        public void ThrowIfConfigIsInvalid()
        {
            if (this.MutualTls != null)
            {
                this.MutualTls.ThrowIfConfigIsInvalid();
            }
        }
    }

    internal class DockerMutualTLSAuth
    {
        public DockerMutualTLSAuth()
        {
            PfxFile = "";
        }

        public string PfxFile { get; set; }
        public string? Password { get; set; }

        public void ThrowIfConfigIsInvalid()
        {
            if (string.IsNullOrWhiteSpace(this.PfxFile))
            {
                throw new Exception("Mutual TLS Auth is missing the certificate pfx file ('pfx_file')");
            }

            System.IO.File.ReadAllText(this.PfxFile);
        }
    }
}
