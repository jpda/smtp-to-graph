using Microsoft.Graph;
using smtp_to_graph;
using SmtpServer;
using SmtpServer.Storage;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging();
builder.Services.AddSingleton(x =>
{
    var graphConfig = builder.Configuration.GetSection("Graph").Get<GraphConfiguration>() ?? throw new InvalidOperationException("Graph configuration is missing");

    var clientSecretCredential = new ConfiguredClientSecretCredential(graphConfig);
    return new GraphServiceClient(clientSecretCredential);//, scopes);
});

builder.Services.AddTransient<IMessageStore, GraphForwardingMessageStore>();
builder.Services.AddSingleton(x =>
    new SmtpServer.SmtpServer(new SmtpServerOptionsBuilder()
    .ServerName("localhost")
    .Port(25)
    .Build(), x.GetRequiredService<IServiceProvider>()));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

