using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NotificationApi.Contracts;
using NotificationApi.Providers;
using NotificationApi.Repositories;
using NotificationApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    // Binding failures (malformed JSON, bad date format) use the same envelope as our own validation errors.
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        return new BadRequestObjectResult(
            new ApiResponse<Dictionary<string, string[]>>(StatusCodes.Status400BadRequest, errors));
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.Configure<EmailProviderOptions>(builder.Configuration.GetSection(EmailProviderOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
// Singleton because it owns the in-memory data.
builder.Services.AddSingleton<INotificationRepository, InMemoryNotificationRepository>();
builder.Services.AddSingleton<INotificationProvider, SimulatedEmailProvider>();
builder.Services.AddScoped<INotificationService, NotificationService>();

var app = builder.Build();

// Unhandled exceptions become a 500 in the standard envelope instead of leaking a stack trace.
// The exception itself is logged by the exception handler middleware.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ApiResponse<string>(StatusCodes.Status500InternalServerError, "An unexpected error occurred."));
}));
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
