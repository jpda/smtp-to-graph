using Azure.Identity;

namespace smtp_to_graph;

public class GraphConfiguration
{
    public string ClientId { get; set; } = null!;
    public required string TenantId { get; set; }
    public required string ClientSecret { get; set; }
    public required string MailboxName { get; set; }
}

public class ConfiguredClientSecretCredential(GraphConfiguration graphConfig, TokenCredentialOptions options) : ClientSecretCredential(graphConfig.TenantId, graphConfig.ClientId, graphConfig.ClientSecret, options)
{
    public ConfiguredClientSecretCredential(GraphConfiguration graphConfig) : this(graphConfig, new ClientSecretCredentialOptions() { AuthorityHost = AzureAuthorityHosts.AzurePublicCloud }) { }
}