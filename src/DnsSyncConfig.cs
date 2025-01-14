using System;
using System.Collections.Generic;
using System.Net;
using Serilog.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dns_sync
{
    public class DnsSyncConfig
    {
        public DnsSyncConfig()
        {
            Plugins = new Dictionary<string, Dictionary<string, object>>();
        }

        public static DnsSyncConfig LoadAndValidate(string path)
        {
            DnsSyncLogger.Information($"Loading config from: {path}");

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var ymlText = System.IO.File.ReadAllText(path);

                var config = deserializer.Deserialize<DnsSyncConfig>(ymlText);

                DnsSyncLogger.Information($"Correctly Loaded config from: {path}");

                config.ThrowIfConfigIsInvalid();

                return config;
            }
            catch (Exception e)
            {
                DnsSyncLogger.Error("There was an error while loading the config", e);
                throw;
            }
        }

        public void ThrowIfConfigIsInvalid()
        {
            if (Hosts == null)
            {
                throw new Exception("Config is missing 'hosts' section");
            }
            if (Hosts.Count == 0)
            {
                throw new Exception("Config does not include any hosts");
            }

            foreach (var h in this.Hosts)
            {
                h.ThrowIfConfigIsInvalid();
            }

            Auth?.ThrowIfConfigIsInvalid();

            OpenObserve?.ThrowIfConfigIsInvalid();

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

        public LogEventLevel? LogLevel { get; set; }

        public IList<DockerHostConfig>? Hosts { get; set; }

        public DockerAuth? Auth { get; set; }

        public Dictionary<string, Dictionary<string, object>> Plugins { get; set; }

        public OpenObserveConfig? OpenObserve { get; set; }
    }

    public class DockerHostConfig
    {
        public DockerHostConfig()
        {
            Uri = "";
        }

        public string Uri { get; set; }
        public string? Hostname { get; set; }
        public string? IpAddress { get; set; }

        public void ThrowIfConfigIsInvalid()
        {
            if (string.IsNullOrWhiteSpace(this.Uri))
            {
                throw new Exception($"Invalid URI provided for docker host: {this.Uri}");
            }

            this.ThrowIfHostnameIsInvalid();
            this.ThrowIfIPAddressIsInvalid();

            if (this.Hostname != null && this.IpAddress != null)
            {
                throw new Exception($"Setup both Hostname and IP Address mapping for host: {this.Uri}");
            }

            var hostUri = new Uri(this.Uri);

            if (Hostname == null && this.IpAddress == null && hostUri.Scheme == "unix")
            {
                throw new Exception($"Cannot infer a hostname from a Unix Socket connection: {this.Uri}");
            }
        }

        public void ThrowIfIPAddressIsInvalid()
        {
            if (IpAddress != null && !IPAddress.TryParse(this.IpAddress, out var ipaddress))
            {
                throw new Exception($"Invalid IP Address provided for host: {this.Uri}");
            }
        }

        public void ThrowIfHostnameIsInvalid()
        {
            if (Hostname != null && string.IsNullOrWhiteSpace(Hostname))
            {
                throw new Exception($"Invalid Hostname Override provided for docker host: {this.Uri}");
            }


        }
    }

    public class OpenObserveConfig
    {
        public OpenObserveConfig()
        {
            Url = "";
            Organization = "default";
            Stream = "";
            Username = "";
            Password = "";
            InstanceHost = Environment.MachineName;
        }
        public string Url { get; set; }
        public string Organization { get; set; }
        public string Stream { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string InstanceHost { get; set; }

        public void ThrowIfConfigIsInvalid()
        {
            if (string.IsNullOrWhiteSpace(this.Url))
            {
                throw new Exception($"Invalid URL provided for open observe: {this.Url}");
            }

            if (string.IsNullOrWhiteSpace(this.Organization))
            {
                throw new Exception($"Open Observe Organization is required");
            }

            if (string.IsNullOrWhiteSpace(this.Stream))
            {
                throw new Exception($"Open Observe Stream is required");
            }

            if (string.IsNullOrWhiteSpace(this.Username))
            {
                throw new Exception($"Open Observe Username is required");
            }

            if (string.IsNullOrWhiteSpace(this.Password))
            {
                throw new Exception($"Open Observe Password is required");
            }
        }
    }

    public class DockerAuth
    {
        public DockerMutualTLSAuth? MutualTls { get; set; }
        public bool? DoNotValidateServerCertificate { get; set; }

        public void ThrowIfConfigIsInvalid()
        {
            if (this.MutualTls != null)
            {
                this.MutualTls.ThrowIfConfigIsInvalid();
            }
        }
    }

    public class DockerMutualTLSAuth
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
