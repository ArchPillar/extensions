using System.Diagnostics;
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Pipelines;
using Command.WebApiSample.Infrastructure;
using Command.WebApiSample.Notes;
using Command.WebApiSample.Notes.Commands;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// Command.WebApiSample
//
// A small ASP.NET Core Minimal API showcasing the recommended way to use
// ArchPillar.Extensions.Commands behind HTTP endpoints:
//
//   - Endpoints depend on ICommandDispatcher and forward the bound command.
//   - Reads bypass the dispatcher (EF Core projections directly).
//   - A user-supplied TransactionMiddleware wraps every dispatch.
//   - Failures are mapped to RFC 7807 problem responses via OperationResult.
//   - Telemetry surfaces every dispatch as a System.Diagnostics.Activity.
//
// The store is a SQLite in-memory database so transactions are real
// (commits/rollbacks observable), without bringing in a server.
// ---------------------------------------------------------------------------

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

// EF Core — keep the connection open for the app lifetime so the in-memory
// SQLite database survives across requests. The container disposes the
// singleton on shutdown.
builder.Services.AddSingleton<Microsoft.Data.Sqlite.SqliteConnection>(_ =>
{
    var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
    connection.Open();
    return connection;
});
builder.Services.AddDbContext<NotesDbContext>((services, options) =>
    options.UseSqlite(services.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>()));

// Commands — register the dispatcher, then handlers, then user middlewares.
builder.Services.AddCommands();
builder.Services.AddCommandHandler<CreateNote, Guid, CreateNoteHandler>();
builder.Services.AddCommandHandler<UpdateNote, UpdateNoteHandler>();
builder.Services.AddCommandHandler<ArchiveNote, ArchiveNoteHandler>();
builder.Services.AddBatchCommandHandler<CreateNote, Guid, CreateNoteBatchHandler>();
builder.Services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Surface every command dispatch as a console activity. In a real service
// this is where OpenTelemetry would attach via .AddSource(CommandActivitySource.Name).
using var activityListener = new ActivityListener
{
    ShouldListenTo = source => source.Name == CommandActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activity => app.Logger.LogInformation(
        "[activity] {Name} status={Status} duration={DurationMs}ms",
        activity.DisplayName,
        activity.Status,
        activity.Duration.TotalMilliseconds),
};
ActivitySource.AddActivityListener(activityListener);

using (IServiceScope scope = app.Services.CreateScope())
{
    NotesDbContext context = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
    await context.Database.EnsureCreatedAsync();
}

// Catch missing handler registrations at startup rather than at first dispatch.
app.Services.ValidateCommandRegistrations();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapNotes();

app.MapGet("/", () => Results.Redirect("/notes"));

await app.RunAsync();
