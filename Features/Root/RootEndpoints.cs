namespace MyBackend.Features.Root;

public static class RootEndpoints
{
    public static IEndpointRouteBuilder MapRootEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => "API Running")
            .WithName("Root");

        return app;
    }
}

