namespace MiniBanking.Api.Common;

internal static class ProblemResponses
{
    /// <summary>Documents the problem responses a money-moving endpoint can return besides validation errors.</summary>
    public static RouteHandlerBuilder ProducesMoneyProblems(this RouteHandlerBuilder builder) => builder
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
