namespace MainCore.Behaviors
{
    public sealed class ErrorLoggingBehavior<TRequest, TResponse>
        : Behavior<TRequest, TResponse>
        where TRequest : ICommand
        where TResponse : IResultBase
    {
        private readonly ILogger _logger;

        public ErrorLoggingBehavior(ILogger logger)
        {
            _logger = logger;
        }

        public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            var response = await Next(request, cancellationToken);

            if (response.IsFailed)
            {
                var message = string.Join(Environment.NewLine, response.Reasons.Select(e => e.Message));
                if (!string.IsNullOrEmpty(message))
                {
                    // 2026-11-15: "Construction queue is full" and the like are planned waits (NextExecuteError), not
                    // problems; they were logged twice as warnings after every upgrade (86 lines in 11 h).
                    if (response.Errors.Count > 0 && response.Errors.All(e => e is MainCore.Errors.NextExecuteError))
                    {
                        _logger.Information("{Message}", message);
                    }
                    else
                    {
                        _logger.Warning("{Message}", message);
                    }
                }
            }

            return response;
        }
    }
}