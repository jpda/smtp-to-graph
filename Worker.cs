namespace smtp_to_graph;

public class Worker(ILogger<Worker> logger, SmtpServer.SmtpServer smtpServer) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly SmtpServer.SmtpServer _smtpServer = smtpServer;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SMTP server");
        await _smtpServer.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stopping SMTP server");
        _smtpServer.Shutdown();
        return Task.CompletedTask;
    }
}